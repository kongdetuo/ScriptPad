using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using System.IO;
using System.Threading;
using Microsoft.CodeAnalysis.Host;
using System.Xml;
using System.Xml.Linq;
using System.Reflection;
using System.Collections.Immutable;
using System.Buffers;
using System.Collections.Concurrent;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using System.Runtime.Serialization;

namespace ScriptPad.Roslyn
{
    internal class ScriptingWorkspace : Workspace
    {
        public static readonly Assembly[] defaultReferences =
        {
            typeof(object).Assembly,                                // mscorelib
            typeof(Uri).Assembly,                                   // System
            typeof(Enumerable).Assembly,                            // System.Core
            typeof(XmlReader).Assembly,                             // System.Xml
            typeof(XDocument).Assembly,                             // System.Xml.Linq
            typeof(Formatter).Assembly,                             // System.Runtime.Serialization
            typeof(ImmutableArray).Assembly,                        // System.Collections.Immutable
            typeof(Span<>).Assembly,                                // System.Memory
            typeof(ArrayPool<>).Assembly,                           // System.Buffers
            Type.GetType("System.ValueTuple", throwOnError: false)?.Assembly ?? typeof(ValueTuple).Assembly     // System.ValueTuple
        };

        private readonly ConcurrentDictionary<string, DocumentationProvider> documentationProviders;

        public ScriptingWorkspace(HostServices hostServices) : base(hostServices, WorkspaceKind.Interactive)
        {
            documentationProviders = new ConcurrentDictionary<string, DocumentationProvider>();
        }

        public new void SetCurrentSolution(Solution solution)
        {
            var oldSolution = CurrentSolution;
            var newSolution = base.SetCurrentSolution(solution);
            RaiseWorkspaceChangedEventAsync(WorkspaceChangeKind.SolutionChanged, oldSolution, newSolution);
        }

        public override bool CanApplyChange(ApplyChangesKind feature)
        {
            return feature == ApplyChangesKind.ChangeDocument || base.CanApplyChange(feature);
        }

        public Document GetDocument(DocumentId id)
        {
            return CurrentSolution.GetDocument(id);
        }

        public DocumentId AddProjectWithDocument(string documentFileName, string text)
        {
            var fileName = Path.GetFileName(documentFileName);
            var name = Path.GetFileNameWithoutExtension(documentFileName);

            var projectId = ProjectId.CreateNewId();

            // ValueTuple needs a separate assembly in .NET 4.6.x. But it is not needed anymore in .NET 4.7+ as it is included in mscorelib.
            var references = defaultReferences.Distinct().Select(CreateReference).ToList();
            references.Add(CreateReference(Assembly.Load("System.Runtime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")));
            references.Add(CreateReference(Assembly.Load("netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51")));
            references.Add(CreateReference(typeof(RuntimeBinderException).Assembly));

            var projectInfo = ProjectInfo.Create(
                projectId,
                VersionStamp.Default,
                name,
                name,
                LanguageNames.CSharp,
                isSubmission: true,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithScriptClassName(name),
                metadataReferences: references,
                parseOptions: new CSharpParseOptions(languageVersion: LanguageVersion.Latest));

            OnProjectAdded(projectInfo);

            var documentId = DocumentId.CreateNewId(projectId);
            var documentInfo = DocumentInfo.Create(
                documentId,
                fileName,
                sourceCodeKind: SourceCodeKind.Script,
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(text, Encoding.UTF8), VersionStamp.Create())));
            OnDocumentAdded(documentInfo);
            return documentId;
        }

        public Project GetProject(DocumentId id)
        {
            return CurrentSolution.GetProject(id.ProjectId);
        }

        public void RemoveProject(DocumentId id)
        {
            OnProjectRemoved(id.ProjectId);
        }

        public void UpdateText(DocumentId documentId, string text)
        {
            OnDocumentTextChanged(documentId, SourceText.From(text, Encoding.UTF8), PreservationMode.PreserveValue);
        }

        public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(DocumentId documentId, CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                var project = CurrentSolution.GetProject(documentId.ProjectId);
                var compilation = await project.GetCompilationAsync(cancellationToken);
                return (IReadOnlyList<Diagnostic>)compilation.GetDiagnostics(cancellationToken);
            }, cancellationToken);
        }

        public Task<BuildResult> BuildAsync(DocumentId documentId, CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                var project = CurrentSolution.GetProject(documentId.ProjectId);
                var compilation = await project.GetCompilationAsync(cancellationToken);

                using (var peStream = new MemoryStream())
                using (var pdbStream = new MemoryStream())
                {
                    var result = compilation.Emit(peStream, pdbStream);
                    var inMemoryAssembly = result.Success ? peStream.ToArray() : null;
                    var inMemorySymbolStore = result.Success ? pdbStream.ToArray() : null;
                    return new BuildResult(result.Diagnostics, inMemoryAssembly, inMemorySymbolStore);
                }
            }, cancellationToken);
        }

        protected override void ApplyDocumentTextChanged(DocumentId documentId, SourceText text)
        {
            OnDocumentTextChanged(documentId, text, PreservationMode.PreserveValue);
        }

        private MetadataReference CreateReference(Assembly assembly)
        {
            string assemblyPath = assembly.Location;
            string documentationPath = Path.ChangeExtension(assemblyPath, "xml");
            var provider = documentationProviders.GetOrAdd(documentationPath, path => new FileBasedXmlDocumentationProvider(path));
            return MetadataReference.CreateFromFile(assemblyPath, new MetadataReferenceProperties(), provider);
        }

        public void AddReference(string path, DocumentId id)
        {
            var data = MetadataReference.CreateFromFile(path);
            var pid = GetDocument(id).Project.Id;
            OnMetadataReferenceAdded(pid, data);
        }

        public void RemoveReference(string path, DocumentId id)
        {
            var project = GetDocument(id).Project;
            var data  = project.MetadataReferences.FirstOrDefault(p => p.Display == path);
            OnMetadataReferenceRemoved(project.Id, data);
        }
    }
}