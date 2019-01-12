using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Search;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Tags;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ScriptPad.Editor;
using ScriptPad.Roslyn;

namespace ScriptPad
{
    /// <summary>
    /// CodeEditor.xaml 的交互逻辑
    /// </summary>
    public partial class CodeEditor : UserControl
    {
        private CompletionWindow completionWindow;
        private CancellationTokenSource completionCancellation;
        private TextMarkerService markerService;

        private static int script;

        public CsScript Script;

        public CodeEditor(string path = null)
        {
            InitializeComponent();
            //codeEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");

            codeEditor.TextArea.TextEntering += textEditor_TextArea_TextEntering;
            codeEditor.TextArea.TextEntered += textEditor_TextArea_TextEntered;
            codeEditor.TextChanged += CodeEditor_TextChanged;

            if (string.IsNullOrEmpty(path))
            {
                script++;
                Script = new CsScript("script" + script, "");
            }
            else
            {
                Script = CsScript.CreateFromFile(path);
            }

            SearchPanel.Install(codeEditor);

            codeEditor.TextArea.IndentationStrategy = new ICSharpCode.AvalonEdit.Indentation.CSharp.CSharpIndentationStrategy(codeEditor.Options);
            //var csFoldingStrategy = new CSharpFoldingStrategy();
            //var foldingManager = FoldingManager.Install(codeEditor.TextArea);

            //DispatcherTimer foldingUpdateTimer = new DispatcherTimer();
            //foldingUpdateTimer.Interval = TimeSpan.FromSeconds(1);
            //foldingUpdateTimer.Tick += (o, e) =>
            //{
            //    csFoldingStrategy.UpdateFoldings(foldingManager, codeEditor.Document);
            //};
            //foldingUpdateTimer.Start();

            //markerService = new TextMarkerService(codeEditor.Document);
            //markerService.AddToTextView(codeEditor.TextArea.TextView);
            markerService = new TextMarkerService(codeEditor);
        }

        /// <summary>
        /// 关闭代码编辑窗口
        /// </summary>
        internal void Close()
        {
            if(Script.IsChanged)
            {
                var result = MessageBox.Show("文件已修改, 是否保存?", "保存", MessageBoxButton.YesNoCancel);
                if (result == MessageBoxResult.OK)
                {
                    Script.Save();
                }
                if(result == MessageBoxResult.Cancel)
                {
                    throw new TaskCanceledException();
                }
            }
        }

        private void CodeEditor_TextChanged(object sender, EventArgs e)
        {
            Script.UpdateText(codeEditor.Text);

            //markerService.RemoveAll(m => true);
            markerService.Clear();
            var diagnostics = Script.GetDiagnostics().Result;

            var listItems = (diagnostics as IEnumerable<Diagnostic>).Where(x => x.Severity != DiagnosticSeverity.Hidden).Select(CreateErrorListItem).ToArray();
            var document = codeEditor.Document;
            foreach (var item in listItems)
            {
                var startOffset = document.GetOffset(new TextLocation(item.StartLine + 1, item.StartColumn + 1));
                var endOffset = document.GetOffset(new TextLocation(item.EndLine + 1, item.EndColumn + 1));

                markerService.Create(startOffset, endOffset - startOffset, item.Description);
                //var marker = markerService.Create(startOffset, endOffset - startOffset);
                //if (marker != null)
                //{
                //    //marker.Tag = args.Id;
                //    marker.MarkerColor = Colors.Red;
                //    marker.ToolTip = item.Description;
                //}
            }
        }

        private void textEditor_TextArea_TextEntering(object sender, TextCompositionEventArgs e)
        {
            if (e.Text.Length > 0 && completionWindow != null)
            {
                if (!IsAllowedLanguageLetter(e.Text[0]))
                {
                    completionWindow.CompletionList.RequestInsertion(e);
                }
            }
        }

        private void textEditor_TextArea_TextEntered(object sender, TextCompositionEventArgs e)
        {
            ShowCompletionAsync(e.Text.FirstOrDefault());
        }

        private Tuple<int, string> GetWord(int position)
        {
            var wordStart = TextUtilities.GetNextCaretPosition(codeEditor.TextArea.Document, position, LogicalDirection.Backward, CaretPositioningMode.WordStart);
            var text = codeEditor.TextArea.Document.GetText(wordStart, position - wordStart);
            return new Tuple<int, string>(wordStart, text);
        }

        private async void ShowCompletionAsync(char? triggerChar)
        {
            //completionCancellation.Cancel();

            completionCancellation = new CancellationTokenSource();
            var cancellationToken = completionCancellation.Token;
            try
            {
                if (completionWindow == null && (triggerChar == null || triggerChar == '.' || IsAllowedLanguageLetter(triggerChar.Value)))
                {
                    var position = codeEditor.CaretOffset;
                    var word = GetWord(position);
                    var completionList = await Script.GetCompletionsAsync(position, cancellationToken: cancellationToken);
                    if (completionList == null || !completionList.Items.Any())
                    {
                        return;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    completionWindow = new CompletionWindow(codeEditor.TextArea)
                    {
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true
                    };
                    completionWindow.MaxWidth = completionWindow.Width = 340;
                    completionWindow.MaxHeight = completionWindow.Height = 206;
                    foreach (var completionItem in completionList.Items)
                    {
                        var data = new CodeCompletionData(completionItem.DisplayText);
                        data.Image = GetImage(completionItem.Tags);
                        data.Description = string.Join("", (await Script.GetDescriptionAsync(completionItem)));
                        completionWindow.CompletionList.CompletionData.Add(data);
                    }

                    if (triggerChar == null || IsAllowedLanguageLetter(triggerChar.Value))
                    {
                        completionWindow.StartOffset = word.Item1;
                        completionWindow.CompletionList.SelectItem(word.Item2);
                    }
                    completionWindow.Show();
                    completionWindow.Closed += (s2, e2) => completionWindow = null;
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static ErrorListItem CreateErrorListItem(Diagnostic diagnostic)
        {
            var mappedSpan = diagnostic.Location.GetMappedLineSpan();
            ErrorSeverity errorSeverity;
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                errorSeverity = ErrorSeverity.Error;
            }
            else if (diagnostic.Severity == DiagnosticSeverity.Warning)
            {
                errorSeverity = ErrorSeverity.Warning;
            }
            else
            {
                errorSeverity = ErrorSeverity.Info;
            }
            return new ErrorListItem(errorSeverity, diagnostic.GetMessage(), mappedSpan.Span.Start.Line, mappedSpan.Span.Start.Character,
                mappedSpan.Span.End.Line, mappedSpan.Span.End.Character);
        }

        private static bool IsAllowedLanguageLetter(char character)
        {
            return TextUtilities.GetCharacterClass(character) == CharacterClass.IdentifierPart;
        }

        private void OpenFile_btn_Click(object sender, RoutedEventArgs e)
        {
        }

        private ImageSource GetImage(ImmutableArray<string> tags)
        {
            var tag = tags.FirstOrDefault();
            if (tag == null) { return null; }

            switch (tag)
            {
                case WellKnownTags.Class:
                    return GetImage("ClassImageSource");

                case WellKnownTags.Constant:
                    return GetImage("ConstantImageSource");

                case WellKnownTags.Delegate:
                    return GetImage("DelegateImageSource");

                case WellKnownTags.Enum:
                    return GetImage("EnumImageSource");

                case WellKnownTags.EnumMember:
                    return GetImage("EnumItemImageSource");

                case WellKnownTags.Event:
                    return GetImage("EventImageSource");

                case WellKnownTags.ExtensionMethod:
                    return GetImage("ExtensionMethodImageSource");

                case WellKnownTags.Field:
                    return GetImage("FieldImageSource");

                case WellKnownTags.Interface:
                    return GetImage("InterfaceImageSource");

                case WellKnownTags.Keyword:
                    return GetImage("KeywordImageSource");

                case WellKnownTags.Method:
                    return GetImage("MethodImageSource");

                case WellKnownTags.Module:
                    return GetImage("ModuleImageSource");

                case WellKnownTags.Namespace:
                    return GetImage("NamespaceImageSource");

                case WellKnownTags.Property:
                    return GetImage("PropertyImageSource");

                case WellKnownTags.Structure:
                    return GetImage("StructureImageSource");
            }
            return null;
        }

        private ImageSource GetImage(string resourceKey)
        {
            var iss = (ImageSource)Resources[resourceKey];
            return iss;
        }

        private async void formatBtn_Click(object sender, RoutedEventArgs e)
        {
            await Script.Format();
            codeEditor.Text = await Script.GetScriptText();
        }

        private async void runBtn_Click(object sender, RoutedEventArgs e)
        {
            if (flowDocument.Blocks.Count == 0)
            {
                flowDocument.Blocks.Add(new Paragraph());
            }

            Console.SetOut(new DelegateTextWriter((flowDocument.Blocks.First() as Paragraph).Inlines.Add));
            var script = CSharpScript.Create(Script.ToCode());
            //await script.RunAsync();
            //script = script.ContinueWith("main(uiApp)");

            await script.RunAsync(Globals.GlobalObject);
        }

        private void SaveFile_btn_Click(object sender, RoutedEventArgs e)
        {
            Script.Save();
        }

        private async void CommentBtn_Click(object sender, RoutedEventArgs e)
        {
            await Script.Comment(codeEditor.SelectionStart, codeEditor.SelectionLength);
            codeEditor.Text = await Script.GetScriptText();
        }

        private async void UnCommentBtn_Click(object sender, RoutedEventArgs e)
        {
            await Script.UnComment(codeEditor.SelectionStart, codeEditor.SelectionLength);
            codeEditor.Text = await Script.GetScriptText();
        }

        private void Reference_Click(object sender, RoutedEventArgs e)
        {
            //var dialog = new System.Windows.Forms.OpenFileDialog();
            //if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            //{
            //    Script.AddReference(dialog.FileName);
            //}

            new ReferenceWindow(this.Script).ShowDialog();
        }
    }
}