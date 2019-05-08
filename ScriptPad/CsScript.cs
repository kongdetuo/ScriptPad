using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition.Hosting;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ScriptPad.Roslyn;

namespace ScriptPad
{
    public class Reference
    {

        public string Name { get; private set; }
        public string Path { get; private set; }

        public static Reference FromFile(string path)
        {
            if (!path.Contains('\\'))
            {
                return new Reference() { Name = path, Path = path };
            }
            else
            {
                var name = path.Substring(path.LastIndexOf('\\') + 1);
                return new Reference() { Name = name, Path = path };
            }
        }

        public static Reference FromCode(string text)
        {
            if (text.Contains('\\'))
            {
                // #r "
                var path = text.Substring(4, text.Length - 5);
                var name = path.Substring(path.LastIndexOf('\\') + 1);
                return new Reference() { Name = name, Path = path };
            }
            else
            {
                // #r "
                var path = text.Substring(4, text.Length - 5);
                return new Reference() { Name = path, Path = path };
            }
        }

        internal string ToCode()
        {
            return "#r \"" + Path + "\"\r\n";
        }
    }

    public class CsScript
    {
        /// <summary>
        /// 是否更改过
        /// </summary>
        public bool IsChanged { get; private set; }
        private readonly DocumentId ID;
        public string Name { get; set; }
        public string Path { get; set; }

        public string Text { get; private set; }

        private List<Reference> references;

        public IReadOnlyCollection<Reference> References => references;

        /// <summary>
        /// 创建具有指定名字和内容的脚本对象
        /// </summary>
        /// <param name="name"></param>
        /// <param name="text"></param>
        public CsScript(string name, string text)
        {
            this.Name = name;
            this.Text = text;
            if (text == null)
                this.Text = "";
            var compositionHost = new ContainerConfiguration().WithAssemblies(MefHostServices.DefaultAssemblies).CreateContainer();
            var hostService = MefHostServices.Create(compositionHost);
            this.Workspace = new ScriptingWorkspace(hostService);
            this.references = new List<Reference>();

            ID = Workspace.AddProjectWithDocument(name, text);
            IsChanged = false;
        }

        private ScriptingWorkspace Workspace { get; set; }

        /// <summary>
        /// 从文件创建 Script 对象
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static CsScript CreateFromFile(string path)
        {
            var info = new FileInfo(path);
            var references = new List<Reference>();

            var text = File.ReadAllLines(path);

            var i = 0;
            for (; i < text.Length; i++)
            {
                if (text[i].StartsWith("#r "))
                {
                    references.Add(Reference.FromCode(text[i]));
                }
                else
                {
                    break;
                }
            }
            var code = new StringBuilder();
            for (; i < text.Length; i++)
            {
                code.Append(text[i]);
                code.Append("\r\n");
            }
            var script = new CsScript(info.Name, code.ToString());
            foreach (var item in references)
            {
                script.AddReference(item);
            }
            return script;
        }

        /// <summary>
        /// 添加引用
        /// </summary>
        /// <param name="path">文件路径</param>
        public void AddReference(string path)
        {
            var reference = Reference.FromFile(path);
            AddReference(reference);
        }

        /// <summary>
        /// 添加引用
        /// </summary>
        /// <param name="reference"></param>
        public void AddReference(Reference reference)
        {
            var value = this.references.Find(r => r.Name == reference.Name);
            if (value != null)
            {
                return;
            }

            this.references.Add(reference);
            Task.Run(()=>Workspace.AddReference(reference.Path, ID));
        }

        /// <summary>
        /// 删除引用
        /// </summary>
        /// <param name="reference"></param>
        public void RemoveReference(Reference reference)
        {
            var value = this.references.Find(r => r.Name == reference.Name);
            if (value == null)
            {
                return;
            }

            this.references.Remove(value);
            Workspace.RemoveReference(reference.Path, ID);
        }

        /// <summary>
        /// 删除引用
        /// </summary>
        /// <param name="path"></param>
        public void RemoveReference(string path)
        {
            var reference = Reference.FromFile(path);
            RemoveReference(reference);
        }

        /// <summary>
        /// 整理脚本
        /// </summary>
        /// <returns></returns>
        public async Task Format()
        {
            var formattedDocument = await Microsoft.CodeAnalysis.Formatting.Formatter.FormatAsync(
                Workspace.GetDocument(ID)).ConfigureAwait(false);
            Workspace.TryApplyChanges(formattedDocument.Project.Solution);
        }

        /// <summary>
        /// 获取自动完成列表
        /// </summary>
        /// <param name="position"></param>
        /// <param name="trigger"></param>
        /// <param name="roles"></param>
        /// <param name="options"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<CompletionList> GetCompletionsAsync(int position, CompletionTrigger trigger = default(CompletionTrigger), ImmutableHashSet<string> roles = null, OptionSet options = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var document = Workspace.GetDocument(ID);
            var completionService = CompletionService.GetService(document);
            return await completionService.GetCompletionsAsync(document, position, cancellationToken: cancellationToken);
        }

        public async Task<ImmutableArray<TaggedText>> GetDescriptionAsync(CompletionItem completionItem)
        {
            var document = Workspace.GetDocument(ID);
            var completionService = CompletionService.GetService(document);
            return (await Task.Run(async()=> (await completionService.GetDescriptionAsync(document, completionItem)))).TaggedParts;
        }

        /// <summary>
        /// 获取诊断信息
        /// </summary>
        /// <returns></returns>
        public async Task<ImmutableArray<Diagnostic>> GetDiagnostics()
        {
            var project = Workspace.GetProject(ID);
            var compilation = await project.GetCompilationAsync();
            return compilation.GetDiagnostics();
        }

        /// <summary>
        /// 获取脚本内容
        /// </summary>
        /// <returns></returns>
        public async Task<string> GetScriptText()
        {
            var text = await Workspace.GetDocument(ID).GetTextAsync();
            return text.ToString();
        }

        /// <summary>
        /// 获取脚本内容
        /// </summary>
        /// <returns></returns>
        public string ToCode()
        {
            var code = new StringBuilder();
            foreach (var item in references)
            {
                code.Append(item.ToCode());
            }

            code.Append(GetScriptText().Result);
            return code.ToString();
        }

        /// <summary>
        /// 保存
        /// </summary>
        public void Save()
        {
            if (string.IsNullOrEmpty(Path))
            {
                var dialog = new SaveFileDialog()
                {
                    Filter = "C# Script|*.csx",
                    Title = "Save File"
                };
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    File.WriteAllText(dialog.FileName, ToCode());
                    this.Path = dialog.FileName;
                }
            }
            else
            {
                File.WriteAllText(Path, ToCode());
            }
            IsChanged = false;
        }

        /// <summary>
        /// 更新脚本内容
        /// </summary>
        /// <param name="newText"></param>
        public void UpdateText(string newText)
        {
            if (Text == newText)
                return;

            IsChanged = true;
            this.Text = newText;
            Workspace.UpdateText(ID, newText);
        }

        public void UpdateText(Document document)
        {
            IsChanged = true;
            Workspace.TryApplyChanges(document.Project.Solution);
        }

        /// <summary>
        /// 注释
        /// </summary>
        /// <param name="selectionStart"></param>
        /// <param name="selectionLength"></param>
        /// <returns></returns>
        internal async Task Comment(int selectionStart, int selectionLength)
        {
            const string singleLineCommentString = "//";
            var document = Workspace.GetDocument(ID);

            var span = new TextSpan(selectionStart, selectionLength);

            var changes = new List<TextChange>();
            var documentText = await document.GetTextAsync().ConfigureAwait(false);
            var lines = documentText.Lines.SkipWhile(x => !x.Span.IntersectsWith(span))
                .TakeWhile(x => x.Span.IntersectsWith(span)).ToArray();

            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(documentText.GetSubText(line.Span).ToString()))
                {
                    changes.Add(new TextChange(new TextSpan(line.Start, 0), singleLineCommentString));
                }
            }

            UpdateText(document.WithText(documentText.WithChanges(changes)));
            //if (changes.Any())
            //{
            //    await Format().ConfigureAwait(false);
            //}
        }

        /// <summary>
        /// 取消注释
        /// </summary>
        /// <param name="selectionStart"></param>
        /// <param name="selectionLength"></param>
        /// <returns></returns>
        internal async Task UnComment(int selectionStart, int selectionLength)
        {
            const string singleLineCommentString = "//";
            var document = Workspace.GetDocument(ID);

            var span = new TextSpan(selectionStart, selectionLength);

            var changes = new List<TextChange>();
            var documentText = await document.GetTextAsync().ConfigureAwait(false);
            var lines = documentText.Lines.SkipWhile(x => !x.Span.IntersectsWith(span))
                .TakeWhile(x => x.Span.IntersectsWith(span)).ToArray();

            foreach (var line in lines)
            {
                var text = documentText.GetSubText(line.Span).ToString();
                if (text.TrimStart().StartsWith(singleLineCommentString, StringComparison.Ordinal))
                {
                    changes.Add(new TextChange(new TextSpan(
                        line.Start + text.IndexOf(singleLineCommentString, StringComparison.Ordinal),
                        singleLineCommentString.Length), string.Empty));
                }
            }

            UpdateText(document.WithText(documentText.WithChanges(changes)));
            //if (changes.Any())
            //{
            //    await Format().ConfigureAwait(false);
            //}
        }

        internal IEnumerable<MetadataReference> GetReferences()
        {
            return Workspace.GetReferences(this.ID);
        }
    }
}