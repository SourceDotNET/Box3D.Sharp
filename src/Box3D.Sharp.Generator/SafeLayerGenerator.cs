using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Box3D.Sharp.Generator
{
    [Generator(LanguageNames.CSharp)]
    public sealed class SafeLayerGenerator : IIncrementalGenerator
    {
        private static readonly ConditionalWeakTable<MetadataReference, GeneratedOutput> s_cache =
            new ConditionalWeakTable<MetadataReference, GeneratedOutput>();

        private static readonly DiagnosticDescriptor s_skipped = new DiagnosticDescriptor(
            "B3G001",
            "Native declaration not mapped",
            "{0}",
            "Box3D.Generator",
            DiagnosticSeverity.Warning,
            true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValueProvider<GeneratedOutput> output = context.CompilationProvider.Select((compilation, _) => Generate(compilation));

            context.RegisterSourceOutput(output, (spc, result) =>
            {
                foreach (KeyValuePair<string, string> file in result.Files)
                {
                    spc.AddSource(file.Key, SourceText.From(file.Value, Encoding.UTF8));
                }

                foreach (string message in result.Messages)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(s_skipped, Location.None, message));
                }
            });
        }

        private static GeneratedOutput Generate(Compilation compilation)
        {
            MetadataReference reference = compilation.References.FirstOrDefault(r =>
                compilation.GetAssemblyOrModuleSymbol(r) is IAssemblySymbol a && a.Name == "Box3D.Interop");
            if (reference == null)
            {
                return GeneratedOutput.Empty;
            }

            return s_cache.GetValue(reference, _ => new Emitter(compilation).Run());
        }
    }

    internal sealed class GeneratedOutput : IEquatable<GeneratedOutput>
    {
        public static readonly GeneratedOutput Empty = new GeneratedOutput(
            ImmutableArray<KeyValuePair<string, string>>.Empty,
            ImmutableArray<string>.Empty);

        public GeneratedOutput(ImmutableArray<KeyValuePair<string, string>> files, ImmutableArray<string> messages)
        {
            Files = files;
            Messages = messages;
        }

        public ImmutableArray<KeyValuePair<string, string>> Files { get; }

        public ImmutableArray<string> Messages { get; }

        public bool Equals(GeneratedOutput other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other == null || Files.Length != other.Files.Length || Messages.Length != other.Messages.Length)
            {
                return false;
            }

            for (int i = 0; i < Files.Length; ++i)
            {
                if (Files[i].Key != other.Files[i].Key || Files[i].Value != other.Files[i].Value)
                {
                    return false;
                }
            }

            return Messages.SequenceEqual(other.Messages);
        }

        public override bool Equals(object obj) => Equals(obj as GeneratedOutput);

        public override int GetHashCode() => Files.Length;
    }
}
