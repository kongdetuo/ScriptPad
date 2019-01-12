using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.CodeAnalysis;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using Microsoft.CodeAnalysis.Tags;
using System.Linq;

namespace ScriptPad.Editor
{
    public class CodeCompletionData : ICompletionData
    {
        public CodeCompletionData(string text)
        {
            this.Text = text;
        }

        public double Priority => 0;

        public string Text { get; }

        public object Description { get; set; }

        public object Content => Text;

        public ImageSource Image { get; set; }

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, Text);
        }
    }
}