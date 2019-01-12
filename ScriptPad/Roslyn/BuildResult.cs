using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ScriptPad.Roslyn
{
    public class BuildResult
    {
        public BuildResult(IReadOnlyList<Diagnostic> diagnostic, byte[] inMemoryAssembly, byte[] inMemorySymbolStore)
        {
            Diagnostic = diagnostic;
            InMemoryAssembly = inMemoryAssembly;
            InMemorySymbolStore = inMemorySymbolStore;
        }

        public IReadOnlyList<Diagnostic> Diagnostic { get; }

        public byte[] InMemoryAssembly { get; }

        public byte[] InMemorySymbolStore { get; }
    }
}