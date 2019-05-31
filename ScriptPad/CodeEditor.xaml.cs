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
using Microsoft.CodeAnalysis.Scripting;
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
            this.Timer.Interval = TimeSpan.FromSeconds(1);
            this.Timer.Tick += Timer_Tick;
            this.Timer.Start();

            // 需要提升效率, 暂时不用
            codeEditor.TextArea.TextEntering += textEditor_TextArea_TextEntering;
            codeEditor.TextArea.TextEntered += textEditor_TextArea_TextEntered;
            codeEditor.TextChanged += CodeEditor_TextChanged;

            if (string.IsNullOrEmpty(path))
            {
                script++;
                Script = new CsScript("script" + script, ScriptGlobals.templateScript);
            }
            else
            {
                Script = CsScript.CreateFromFile(path);
            }

            this.codeEditor.Text = Script.Text;
            SearchPanel.Install(codeEditor);

            //codeEditor.TextArea.IndentationStrategy = new ICSharpCode.AvalonEdit.Indentation.CSharp.CSharpIndentationStrategy(codeEditor.Options);
            codeEditor.TextArea.IndentationStrategy = new CSIndentationStrategy();

            // 这个也有效率问题
            //var csFoldingStrategy = new CSharpFoldingStrategy();
            //var foldingManager = FoldingManager.Install(codeEditor.TextArea);

            //DispatcherTimer foldingUpdateTimer = new DispatcherTimer();
            //foldingUpdateTimer.Interval = TimeSpan.FromSeconds(1);
            //foldingUpdateTimer.Tick += (o, e) =>
            //{
            //    csFoldingStrategy.UpdateFoldings(foldingManager, codeEditor.Document);
            //};
            //foldingUpdateTimer.Start();

            // 需要提升效率
            markerService = new TextMarkerService(codeEditor);
        }

        private async void Timer_Tick(object sender, EventArgs e)
        {
            var document = codeEditor.Document;


            markerService.Clear();
            var diagnostics = await Script.GetDiagnostics();

            var listItems = (diagnostics as IEnumerable<Diagnostic>).Where(x => x.Severity != DiagnosticSeverity.Hidden).Select(CreateErrorListItem).ToArray();
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

        DispatcherTimer Timer = new DispatcherTimer();

        /// <summary>
        /// 关闭代码编辑窗口
        /// </summary>
        internal void Close()
        {
            if (Script.IsChanged)
            {
                var result = MessageBox.Show("文件已修改, 是否保存?", "保存", MessageBoxButton.YesNoCancel);
                if (result == MessageBoxResult.OK)
                {
                    Script.Save();
                }
                if (result == MessageBoxResult.Cancel)
                {
                    throw new TaskCanceledException();
                }
            }
        }

        private void CodeEditor_TextChanged(object sender, EventArgs e)
        {
            Script.UpdateText(codeEditor.Text);
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

        private async void textEditor_TextArea_TextEntered(object sender, TextCompositionEventArgs e)
        {

            try
            {
                char? triggerChar = e.Text.FirstOrDefault();

                completionCancellation = new CancellationTokenSource();
                var cancellationToken = completionCancellation.Token;
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
                        //data.Description = string.Join("", (await Script.GetDescriptionAsync(completionItem)));
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

        private Tuple<int, string> GetWord(int position)
        {
            var wordStart = TextUtilities.GetNextCaretPosition(codeEditor.TextArea.Document, position, LogicalDirection.Backward, CaretPositioningMode.WordStart);
            var text = codeEditor.TextArea.Document.GetText(wordStart, position - wordStart);
            return new Tuple<int, string>(wordStart, text);
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
            System.Windows.Forms.OpenFileDialog openFile = new System.Windows.Forms.OpenFileDialog()
            {
                Filter = "C# 脚本文件(*.csx)|*.csx"
            };
            if (openFile.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                this.Script = CsScript.CreateFromFile(openFile.FileName);
                this.codeEditor.Text = Script.Text;
            }
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
            try
            {
                if (flowDocument.Blocks.Count == 0)
                {
                    flowDocument.Blocks.Add(new Paragraph());
                }
                Console.SetOut(new DelegateTextWriter((flowDocument.Blocks.First() as Paragraph).Inlines.Add));

                var options = ScriptOptions.Default;
                options = options.AddReferences(Script.GetReferences());
                options = options.AddReferences(ScriptGlobals.InitAssemblies);

                var script = CSharpScript.Create(await Script.GetScriptText(), options, globalsType: ScriptGlobals.GlobalObject.GetType());

                if (!string.IsNullOrWhiteSpace(ScriptGlobals.StartScript))
                    script = script.ContinueWith(ScriptGlobals.StartScript, options);

                await script.RunAsync(ScriptGlobals.GlobalObject);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
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

    class CSIndentationStrategy : ICSharpCode.AvalonEdit.Indentation.IIndentationStrategy
    {

        string IndentationString = "\t";

        public void IndentLine(ICSharpCode.AvalonEdit.Document.TextDocument document, DocumentLine line)
        {
            if (document == null)
                throw new ArgumentNullException("document");
            if (line == null)
                throw new ArgumentNullException("line");
            DocumentLine previousLine = line.PreviousLine;
            if (previousLine != null)
            {
                ISegment indentationSegment = TextUtilities.GetWhitespaceAfter(document, previousLine.Offset);
                string indentation = document.GetText(indentationSegment);

                var c = document.GetCharAt(previousLine.EndOffset - 1);
                if(c == '{')
                {
                    indentation += IndentationString;
                }

                // copy indentation to line
                indentationSegment = TextUtilities.GetWhitespaceAfter(document, line.Offset);
                document.Replace(indentationSegment.Offset, indentationSegment.Length, indentation,
                                 OffsetChangeMappingType.RemoveAndInsert);
                // OffsetChangeMappingType.RemoveAndInsert guarantees the caret moves behind the new indentation.
            }
        }

        public void IndentLines(ICSharpCode.AvalonEdit.Document.TextDocument document, int beginLine, int endLine)
        {

        }
    }

}