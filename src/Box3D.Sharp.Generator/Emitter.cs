using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Box3D.Sharp.Generator
{
    internal sealed partial class Emitter
    {
        private const string Usings =
            "using System;\n" +
            "using System.Numerics;\n" +
            "using System.Runtime.CompilerServices;\n" +
            "using System.Runtime.InteropServices;\n" +
            "using Box3D.Interop;\n\n" +
            "namespace Box3D;\n\n";

        private static readonly HashSet<string> s_excludedFunctions = new HashSet<string>(StringComparer.Ordinal)
        {
            "b3InternalAssert", "b3DestroyWorld",
            "b3CreateWorld", "b3World_Draw", "b3RecPlayer_DrawFrameQueries", "b3RecPlayer_SetDebugShapeCallbacks",
            "b3World_SetCustomFilterCallback", "b3World_SetPreSolveCallback",
            "b3SetAllocator", "b3SetAssertFcn", "b3SetLogFcn",
            "b3World_SetUserData", "b3World_GetUserData",
            "b3GetCompoundMaterials", "b3ConvertCompoundToBytes", "b3ConvertBytesToCompound", "b3CreateCompound", "b3Recording_GetData",
            "b3Hull2D", "b3SimplifyHull2D", "b3DynamicTree_Destroy",
        };
        private static readonly HashSet<string> s_excludedConstants = new HashSet<string>(StringComparer.Ordinal)
        {
            "B3_PI", "B3_DEG_TO_RAD", "B3_RAD_TO_DEG", "B3_MIN_SCALE",
        };
        private static readonly Dictionary<string, string[][]> s_offsetArrays = new Dictionary<string, string[][]>(StringComparer.Ordinal)
        {
            ["b3HullData"] = new[]
            {
                new[] { "Vertices", "HullVertex", "vertexOffset", "Pointer->vertexCount" },
                new[] { "Points", "Vector3", "pointOffset", "Pointer->vertexCount" },
                new[] { "Edges", "HullHalfEdge", "edgeOffset", "Pointer->edgeCount" },
                new[] { "Planes", "Plane", "planeOffset", "Pointer->faceCount" },
                new[] { "Faces", "HullFace", "faceOffset", "Pointer->faceCount" },
                new[] { "SoaVertices", "float", "soaVertexOffset", "3 * ((Pointer->vertexCount + 7) & ~7)" },
                new[] { "SoaNormals", "float", "soaNormalOffset", "3 * ((Pointer->faceCount + 7) & ~7)" },
                new[] { "EdgeCosines", "float", "edgeCosineOffset", "Pointer->edgeCount / 2" },
            },
            ["b3MeshData"] = new[]
            {
                new[] { "Nodes", "MeshNode", "nodeOffset", "Pointer->nodeCount" },
                new[] { "Vertices", "Vector3", "vertexOffset", "Pointer->vertexCount" },
                new[] { "Triangles", "MeshTriangle", "triangleOffset", "Pointer->triangleCount" },
                new[] { "MaterialIndices", "byte", "materialOffset", "Pointer->triangleCount" },
                new[] { "Flags", "byte", "flagsOffset", "Pointer->triangleCount" },
            },
            ["b3HeightFieldData"] = new[]
            {
                new[] { "CompressedHeights", "ushort", "heightsOffset", "Pointer->columnCount * Pointer->rowCount" },
                new[] { "MaterialIndices", "byte", "materialOffset", "(Pointer->columnCount - 1) * (Pointer->rowCount - 1)" },
                new[] { "Flags", "byte", "flagsOffset", "2 * (Pointer->columnCount - 1) * (Pointer->rowCount - 1)" },
            },
        };

        private readonly NativeModel _model;
        private readonly List<KeyValuePair<string, string>> _files = new List<KeyValuePair<string, string>>();
        private readonly Dictionary<string, Target> _targets = new Dictionary<string, Target>(StringComparer.Ordinal);
        private readonly List<string> _typedJoints = new List<string>();
        private readonly Dictionary<string, bool> _spanStructs = new Dictionary<string, bool>(StringComparer.Ordinal);
        private HashSet<string> _arrayElements;

        public Emitter(Compilation compilation)
        {
            _model = new NativeModel(compilation);
        }

        public GeneratedOutput Run()
        {
            if (_model.Native == null)
            {
                return GeneratedOutput.Empty;
            }

            EmitEnums();
            EmitIds();
            EmitHandles();
            EmitStructs();
            EmitConstants();
            EmitFunctions();
            EmitFunctionTypes();
            EmitHandlers();

            return new GeneratedOutput(
                _files.OrderBy(f => f.Key, StringComparer.Ordinal).ToImmutableArray(),
                _model.Messages.ToImmutableArray());
        }

        private void AddFile(string name, string body)
        {
            _files.Add(new KeyValuePair<string, string>(name + ".g.cs", Usings + body));
        }

        private void Skip(string message)
        {
            _model.Messages.Add(message);
        }

        internal static string Escape(string name)
        {
            return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
                ? "@" + name
                : name;
        }

        internal static string Pascal(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        internal static string Camel(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            int upper = 0;
            while (upper < name.Length && char.IsUpper(name[upper]))
            {
                ++upper;
            }

            if (upper <= 1)
            {
                return char.ToLowerInvariant(name[0]) + name.Substring(1);
            }

            if (upper == name.Length)
            {
                return name.ToLowerInvariant();
            }

            return name.Substring(0, upper - 1).ToLowerInvariant() + name.Substring(upper - 1);
        }

        private static string Full(ITypeSymbol type)
        {
            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private void EmitEnums()
        {
            var sb = new StringBuilder();
            foreach (INamedTypeSymbol type in _model.Types.Values.Where(t => t.TypeKind == TypeKind.Enum).OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                string name = NativeModel.StripPrefix(type.Name);
                List<IFieldSymbol> members = type.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsConst).ToList();
                List<string> names = EnumMemberNames(type, members.Select(m => m.Name).ToList());
                if (name.EndsWith("Flags", StringComparison.Ordinal))
                {
                    sb.Append("[Flags]\n");
                }

                sb.Append("public enum ").Append(name).Append(" : ").Append(type.EnumUnderlyingType.ToDisplayString()).Append("\n{\n");
                for (int i = 0; i < members.Count; ++i)
                {
                    sb.Append("    ").Append(names[i]).Append(" = ").Append(Convert.ToString(members[i].ConstantValue, CultureInfo.InvariantCulture)).Append(",\n");
                }

                sb.Append("}\n\n");
            }

            AddFile("Enums", sb.ToString());
        }

        private static List<string> EnumMemberNames(INamedTypeSymbol type, List<string> raw)
        {
            string enumBase = Camel(NativeModel.StripPrefix(type.Name));
            List<string> stripped = raw.Select(r => r.StartsWith("b3_", StringComparison.Ordinal) ? r.Substring(3) : r).ToList();
            List<int> regular = Enumerable.Range(0, stripped.Count)
                .Where(i => !(stripped[i].EndsWith("Count", StringComparison.Ordinal) && stripped[i].StartsWith(enumBase, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            string prefix = CommonWordPrefix(regular.Select(i => stripped[i]).ToList());
            string suffix = CommonWordSuffix(regular.Select(i => stripped[i]).ToList());
            var result = new List<string>();
            for (int i = 0; i < stripped.Count; ++i)
            {
                string s = stripped[i];
                if (!regular.Contains(i))
                {
                    result.Add("Count");
                    continue;
                }

                string trimmed = s;
                if (prefix.Length > 0 && trimmed.Length > prefix.Length)
                {
                    trimmed = trimmed.Substring(prefix.Length);
                }

                if (suffix.Length > 0 && trimmed.Length > suffix.Length)
                {
                    trimmed = trimmed.Substring(0, trimmed.Length - suffix.Length);
                }

                trimmed = Pascal(trimmed);
                if (trimmed.Length == 0 || char.IsDigit(trimmed[0]))
                {
                    trimmed = Pascal(s);
                }

                result.Add(trimmed);
            }

            return result;
        }

        private static string CommonWordPrefix(List<string> names)
        {
            if (names.Count < 2)
            {
                return string.Empty;
            }

            string first = names[0];
            int best = 0;
            for (int i = 1; i < first.Length; ++i)
            {
                if (!char.IsUpper(first[i]))
                {
                    continue;
                }

                string candidate = first.Substring(0, i);
                if (names.All(n => n.Length > i && n.StartsWith(candidate, StringComparison.Ordinal) && char.IsUpper(n[i])))
                {
                    best = i;
                }
            }

            return first.Substring(0, best);
        }

        private static string CommonWordSuffix(List<string> names)
        {
            if (names.Count < 2)
            {
                return string.Empty;
            }

            string first = names[0];
            string best = string.Empty;
            for (int i = first.Length - 1; i > 0; --i)
            {
                if (!char.IsUpper(first[i]))
                {
                    continue;
                }

                string candidate = first.Substring(i);
                if (names.All(n => n.Length > candidate.Length && n.EndsWith(candidate, StringComparison.Ordinal)))
                {
                    best = candidate;
                }
            }

            return best;
        }

        private void EmitIds()
        {
            var sb = new StringBuilder();
            foreach (INamedTypeSymbol type in _model.Types.Values.Where(t => _model.Categories.TryGetValue(t.Name, out TypeCategory c) && c == TypeCategory.Id).OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                string name = _model.ManagedTypeName(type.Name);
                (int size, int _) = _model.Layout(type);
                if (size == 4 || size == 8)
                {
                    sb.Append("public enum ").Append(name).Append(" : ").Append(size == 4 ? "uint" : "ulong").Append("\n{\n    Null = 0,\n}\n\n");
                    AppendIsValid(sb, name);
                    continue;
                }

                List<IFieldSymbol> fields = NativeModel.InstanceFields(type).Where(f => !f.Name.StartsWith("padding", StringComparison.Ordinal)).ToList();
                sb.Append("public readonly struct ").Append(name).Append(" : IEquatable<").Append(name).Append(">\n{\n");
                sb.Append("    internal readonly ").Append(Full(type)).Append(" Value;\n\n");
                sb.Append("    internal ").Append(name).Append("(").Append(Full(type)).Append(" value)\n    {\n        Value = value;\n    }\n\n");
                sb.Append("    public static ").Append(name).Append(" Null => default;\n\n");
                sb.Append("    public bool IsNull => Equals(default);\n\n");
                sb.Append("    public bool IsValid => !IsNull;\n\n");
                sb.Append("    public bool Equals(").Append(name).Append(" other) => ")
                    .Append(string.Join(" && ", fields.Select(f => "Value." + f.Name + " == other.Value." + f.Name))).Append(";\n\n");
                sb.Append("    public override bool Equals(object obj) => obj is ").Append(name).Append(" other && Equals(other);\n\n");
                sb.Append("    public override int GetHashCode() => HashCode.Combine(")
                    .Append(string.Join(", ", fields.Select(f => "Value." + f.Name))).Append(");\n\n");
                sb.Append("    public static bool operator ==(").Append(name).Append(" left, ").Append(name).Append(" right) => left.Equals(right);\n\n");
                sb.Append("    public static bool operator !=(").Append(name).Append(" left, ").Append(name).Append(" right) => !left.Equals(right);\n");
                sb.Append("}\n\n");
            }

            foreach (IMethodSymbol function in _model.Functions)
            {
                int underscore = function.Name.IndexOf('_');
                if (underscore < 0)
                {
                    continue;
                }

                string prefix = function.Name.Substring(2, underscore - 2);
                if (prefix.EndsWith("Joint", StringComparison.Ordinal) && prefix != "Joint" && !_typedJoints.Contains(prefix))
                {
                    _typedJoints.Add(prefix);
                }
            }

            foreach (IMethodSymbol function in _model.Functions)
            {
                if (function.Name.StartsWith("b3Create", StringComparison.Ordinal) && function.Name.EndsWith("Joint", StringComparison.Ordinal))
                {
                    string prefix = function.Name.Substring("b3Create".Length);
                    if (!_typedJoints.Contains(prefix))
                    {
                        _typedJoints.Add(prefix);
                    }
                }
            }

            _typedJoints.Sort(StringComparer.Ordinal);
            foreach (string joint in _typedJoints)
            {
                sb.Append("public enum ").Append(joint).Append(" : ulong\n{\n    Null = 0,\n}\n\n");
                AppendIsValid(sb, joint);
            }

            AddFile("Ids", sb.ToString());
        }

        private static void AppendIsValid(StringBuilder sb, string name)
        {
            sb.Append("public static unsafe partial class ").Append(name).Append("Extensions\n{\n    extension(").Append(name)
                .Append(" id)\n    {\n        public bool IsValid => id != ").Append(name).Append(".Null;\n    }\n}\n\n");
        }

        private void EmitHandles()
        {
            var sb = new StringBuilder();
            foreach (INamedTypeSymbol type in _model.Types.Values.Where(t => _model.Categories.TryGetValue(t.Name, out TypeCategory c) && c == TypeCategory.Handle).OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                string name = _model.ManagedTypeName(type.Name);
                sb.Append("public readonly unsafe partial struct ").Append(name).Append(" : IEquatable<").Append(name).Append(">\n{\n");
                sb.Append("    internal readonly ").Append(Full(type)).Append("* Pointer;\n\n");
                sb.Append("    internal ").Append(name).Append("(").Append(Full(type)).Append("* pointer)\n    {\n        Pointer = pointer;\n    }\n\n");
                sb.Append("    public bool IsNull => Pointer == null;\n\n");
                s_offsetArrays.TryGetValue(type.Name, out string[][] arrays);
                foreach ((IFieldSymbol field, int _) in _model.FieldOffsets(type))
                {
                    if (arrays != null && arrays.Any(a => a[2] == field.Name))
                    {
                        continue;
                    }

                    TypeCategory category = _model.CategoryOf(field.Type);
                    if (category == TypeCategory.Primitive || category == TypeCategory.Numerics || category == TypeCategory.Enum
                        || category == TypeCategory.Id || category == TypeCategory.Plain)
                    {
                        string propertyName = Pascal(field.Name);
                        if (propertyName == "Pointer" || propertyName == "IsNull")
                        {
                            continue;
                        }

                        sb.Append("    public ").Append(Managed(field.Type)).Append(" ").Append(propertyName).Append(" => ")
                            .Append(FromNative(field.Type, "Pointer->" + Escape(field.Name))).Append(";\n\n");
                    }
                }

                if (arrays != null)
                {
                    foreach (string[] array in arrays)
                    {
                        sb.Append("    public ReadOnlySpan<").Append(array[1]).Append("> ").Append(array[0]).Append(" => Pointer->").Append(array[2])
                            .Append(" == 0 ? default : new ReadOnlySpan<").Append(array[1]).Append(">((byte*)Pointer + Pointer->").Append(array[2])
                            .Append(", ").Append(array[3]).Append(");\n\n");
                    }
                }

                sb.Append("    public bool Equals(").Append(name).Append(" other) => Pointer == other.Pointer;\n\n");
                sb.Append("    public override bool Equals(object obj) => obj is ").Append(name).Append(" other && Equals(other);\n\n");
                sb.Append("    public override int GetHashCode() => ((nint)Pointer).GetHashCode();\n\n");
                sb.Append("    public static bool operator ==(").Append(name).Append(" left, ").Append(name).Append(" right) => left.Pointer == right.Pointer;\n\n");
                sb.Append("    public static bool operator !=(").Append(name).Append(" left, ").Append(name).Append(" right) => left.Pointer != right.Pointer;\n");
                sb.Append("}\n\n");
            }

            AddFile("Handles", sb.ToString());
        }

        private void EmitConstants()
        {
            var sb = new StringBuilder();
            sb.Append("public static class Constants\n{\n");
            foreach (IFieldSymbol field in _model.Native.GetMembers().OfType<IFieldSymbol>().Where(f => f.Name.StartsWith("B3_", StringComparison.Ordinal)))
            {
                if (s_excludedConstants.Contains(field.Name))
                {
                    continue;
                }

                string name = ConstantName(field.Name);
                string type = field.Type.ToDisplayString();
                if (field.IsConst)
                {
                    sb.Append("    public const ").Append(type).Append(" ").Append(name).Append(" = global::Box3D.Interop.Native.").Append(field.Name).Append(";\n\n");
                    continue;
                }

                string expression = ConstantExpression(NativeModel.NativeTypeNameOf(field), field.Name);
                sb.Append("    public static ").Append(type).Append(" ").Append(name).Append(" => ")
                    .Append(expression ?? "global::Box3D.Interop.Native." + field.Name).Append(";\n\n");
            }

            sb.Append("}\n");
            AddFile("Constants", sb.ToString());
        }

        private static string ConstantName(string macro)
        {
            string[] parts = macro.Substring(3).Split('_');
            return string.Concat(parts.Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant()));
        }

        private static string ConstantExpression(string define, string name)
        {
            if (define == null)
            {
                return null;
            }

            string prefix = "#define " + name;
            if (!define.StartsWith(prefix, StringComparison.Ordinal))
            {
                return null;
            }

            string expression = define.Substring(prefix.Length).Trim()
                .Replace("b3GetLengthUnitsPerMeter()", "global::Box3D.Interop.Native.b3GetLengthUnitsPerMeter()");
            string check = expression.Replace("global::Box3D.Interop.Native.b3GetLengthUnitsPerMeter()", string.Empty);
            foreach (char c in check)
            {
                if (char.IsLetter(c) && c != 'f' && c != 'e' && c != 'U' && c != 'L' && c != 'x' && c != 'X' && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F'))
                {
                    return null;
                }
            }

            return expression;
        }
    }
}
