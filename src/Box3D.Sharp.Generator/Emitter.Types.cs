using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Box3D.Sharp.Generator
{
    internal sealed partial class Emitter
    {
        private string Managed(ITypeSymbol type)
        {
            if (type is IPointerTypeSymbol pointer && _model.IsHandlePointer(type))
            {
                return _model.ManagedTypeName(pointer.PointedAtType.Name);
            }

            switch (_model.CategoryOf(type))
            {
                case TypeCategory.Primitive:
                    if (type.SpecialType == SpecialType.System_UIntPtr)
                    {
                        return "ulong";
                    }

                    if (type.SpecialType == SpecialType.System_IntPtr)
                    {
                        return "long";
                    }

                    return type.ToDisplayString();
                case TypeCategory.Numerics:
                    return type.Name;
                case TypeCategory.Enum:
                case TypeCategory.Id:
                case TypeCategory.Plain:
                case TypeCategory.Opaque:
                case TypeCategory.Managed:
                    return _model.ManagedTypeName(type.Name);
                default:
                    return null;
            }
        }

        private string CallbackType(ITypeSymbol type)
        {
            if (type is IFunctionPointerTypeSymbol function)
            {
                return ManagedFunctionPointer(function);
            }

            if (type.SpecialType == SpecialType.System_Void)
            {
                return "void";
            }

            if (type is IPointerTypeSymbol pointer)
            {
                if (_model.IsHandlePointer(type))
                {
                    return Managed(type);
                }

                TypeCategory pointee = _model.CategoryOf(pointer.PointedAtType);
                if (pointee == TypeCategory.Plain || pointee == TypeCategory.Numerics)
                {
                    return Managed(pointer.PointedAtType) + "*";
                }

                return "IntPtr";
            }

            return Managed(type) ?? Full(type);
        }

        private readonly Dictionary<string, string> _functionTypes = new Dictionary<string, string>(StringComparer.Ordinal);

        private string FunctionWrapper(IFunctionPointerTypeSymbol function, string nativeTypeName, string fallbackName)
        {
            string name = fallbackName;
            if (nativeTypeName != null && nativeTypeName.StartsWith("b3", StringComparison.Ordinal))
            {
                name = NativeModel.StripPrefix(nativeTypeName.Replace("*", string.Empty).Trim());
            }

            string signature = ManagedFunctionPointer(function);
            if (!_functionTypes.ContainsKey(name))
            {
                _functionTypes[name] = signature;
            }

            return name;
        }

        private void EmitFunctionTypes()
        {
            if (_functionTypes.Count == 0)
            {
                return;
            }

            var sb = new StringBuilder();
            foreach (KeyValuePair<string, string> type in _functionTypes.OrderBy(t => t.Key, StringComparer.Ordinal))
            {
                sb.Append("public readonly unsafe struct ").Append(type.Key).Append("\n{\n");
                sb.Append("    public readonly ").Append(type.Value).Append(" Function;\n\n");
                sb.Append("    public ").Append(type.Key).Append("(").Append(type.Value).Append(" function)\n    {\n        Function = function;\n    }\n\n");
                sb.Append("    public bool IsNull => Function == null;\n}\n\n");
            }

            AddFile("FunctionTypes", sb.ToString());
        }

        private string ManagedFunctionPointer(IFunctionPointerTypeSymbol function)
        {
            IMethodSymbol signature = function.Signature;
            IEnumerable<string> types = signature.Parameters.Select(p => CallbackType(p.Type)).Concat(new[] { CallbackType(signature.ReturnType) });
            return "delegate* unmanaged[Cdecl]<" + string.Join(", ", types) + ">";
        }

        private bool IsSpanStruct(StructInfo info)
        {
            if (_spanStructs.TryGetValue(info.NativeName, out bool cached))
            {
                return cached;
            }

            if (_arrayElements == null)
            {
                _arrayElements = new HashSet<string>(
                    _model.Structs.Values.SelectMany(s => s.Fields).Where(f => f.Kind == FieldKind.Array && f.Element != null).Select(f => f.Element.Name),
                    StringComparer.Ordinal);
            }

            _spanStructs[info.NativeName] = false;
            bool result = !_arrayElements.Contains(info.NativeName) && info.Fields.Any(f =>
                (f.Kind == FieldKind.Array && Managed(f.Element) != null)
                || (f.Kind == FieldKind.Value && _model.CategoryOf(f.Type) == TypeCategory.Managed
                    && _model.Structs.TryGetValue(f.Type.Name, out StructInfo nested) && IsSpanStruct(nested)));
            _spanStructs[info.NativeName] = result;
            return result;
        }

        private static bool IsConstPointer(FieldInfo field)
        {
            string nativeName = field.Symbol == null ? null : NativeModel.NativeTypeNameOf(field.Symbol);
            return nativeName != null && nativeName.StartsWith("const ", StringComparison.Ordinal);
        }

        private bool IsBlittableManaged(ITypeSymbol type)
        {
            TypeCategory category = _model.CategoryOf(type);
            return category == TypeCategory.Primitive || category == TypeCategory.Numerics || category == TypeCategory.Enum
                || category == TypeCategory.Id || category == TypeCategory.Plain || category == TypeCategory.Opaque
                || _model.IsHandlePointer(type);
        }

        private bool IsStructId(ITypeSymbol type)
        {
            return _model.CategoryOf(type) == TypeCategory.Id && _model.Layout(type).size != 4 && _model.Layout(type).size != 8;
        }

        private string FromNative(ITypeSymbol type, string expression)
        {
            if (_model.IsHandlePointer(type))
            {
                return "new " + Managed(type) + "(" + expression + ")";
            }

            if (type.SpecialType == SpecialType.System_UIntPtr || type.SpecialType == SpecialType.System_IntPtr)
            {
                return "(" + Managed(type) + ")" + expression;
            }

            switch (_model.CategoryOf(type))
            {
                case TypeCategory.Primitive:
                case TypeCategory.Numerics:
                    return expression;
                case TypeCategory.Enum:
                    return "(" + Managed(type) + ")" + expression;
                case TypeCategory.Id:
                    return IsStructId(type)
                        ? "new " + Managed(type) + "(" + expression + ")"
                        : "Unsafe.BitCast<" + Full(type) + ", " + Managed(type) + ">(" + expression + ")";
                case TypeCategory.Plain:
                case TypeCategory.Opaque:
                    return "Unsafe.BitCast<" + Full(type) + ", " + Managed(type) + ">(" + expression + ")";
                case TypeCategory.Managed:
                    return Managed(type) + ".FromNative(" + expression + ")";
                default:
                    return null;
            }
        }

        private string ToNative(ITypeSymbol type, string expression)
        {
            if (_model.IsHandlePointer(type))
            {
                return expression + ".Pointer";
            }

            if (type.SpecialType == SpecialType.System_UIntPtr || type.SpecialType == SpecialType.System_IntPtr)
            {
                return "(" + type.ToDisplayString() + ")" + expression;
            }

            switch (_model.CategoryOf(type))
            {
                case TypeCategory.Primitive:
                case TypeCategory.Numerics:
                    return expression;
                case TypeCategory.Enum:
                    return "(" + Full(type) + ")" + expression;
                case TypeCategory.Id:
                    return IsStructId(type)
                        ? expression + ".Value"
                        : "Unsafe.BitCast<" + Managed(type) + ", " + Full(type) + ">(" + expression + ")";
                case TypeCategory.Plain:
                case TypeCategory.Opaque:
                    return "Unsafe.BitCast<" + Managed(type) + ", " + Full(type) + ">(" + expression + ")";
                default:
                    return null;
            }
        }

        private string DefaultRawExpression(StructInfo info)
        {
            return info.DefaultFunction != null ? "global::Box3D.Interop.Native." + info.DefaultFunction.Name + "()" : "default";
        }

        private void EmitStructs()
        {
            var sb = new StringBuilder();
            foreach (StructInfo info in _model.Structs.Values.OrderBy(s => s.NativeName, StringComparer.Ordinal))
            {
                switch (_model.Categories[info.NativeName])
                {
                    case TypeCategory.Plain:
                        EmitPlainStruct(sb, info);
                        break;
                    case TypeCategory.Opaque:
                        sb.Append("[StructLayout(LayoutKind.Explicit, Size = ").Append(info.Size).Append(")]\n");
                        sb.Append("public struct ").Append(info.ManagedName).Append("\n{\n    [FieldOffset(0)]\n    private byte _data;\n}\n\n");
                        break;
                    case TypeCategory.Managed:
                        EmitManagedStruct(sb, info);
                        break;
                }
            }

            AddFile("Structs", sb.ToString());
        }

        private void EmitPlainStruct(StringBuilder sb, StructInfo info)
        {
            var members = new StringBuilder();
            var nested = new StringBuilder();
            foreach (FieldInfo field in info.Fields)
            {
                EmitPlainField(members, nested, info, field);
            }

            if (info.DefaultFunction != null)
            {
                members.Append("    public static ").Append(info.ManagedName).Append(" Default => Unsafe.BitCast<")
                    .Append(Full(info.Symbol)).Append(", ").Append(info.ManagedName).Append(">(global::Box3D.Interop.Native.")
                    .Append(info.DefaultFunction.Name).Append("());\n\n");
            }

            sb.Append("[StructLayout(LayoutKind.Explicit, Size = ").Append(info.Size).Append(")]\n");
            sb.Append("public unsafe partial struct ").Append(info.ManagedName).Append("\n{\n");
            sb.Append(members);
            sb.Append(nested);
            TrimBlankLine(sb);
            sb.Append("}\n\n");
        }

        private static void TrimBlankLine(StringBuilder sb)
        {
            if (sb.Length >= 2 && sb[sb.Length - 1] == '\n' && sb[sb.Length - 2] == '\n')
            {
                sb.Length -= 1;
            }
        }

        private void EmitPlainField(StringBuilder members, StringBuilder nested, StructInfo owner, FieldInfo field)
        {
            string name = Escape(field.Name);
            string offset = "    [FieldOffset(" + field.Offset + ")]\n";
            switch (field.Kind)
            {
                case FieldKind.Value:
                case FieldKind.Count:
                {
                    string type = Managed(field.Type);
                    if (type == null || _model.CategoryOf(field.Type) == TypeCategory.Managed)
                    {
                        Skip(owner.NativeName + "." + field.Name + ": unsupported field type " + field.Type.ToDisplayString());
                        members.Append(offset).Append("    private ").Append(Full(field.Type)).Append(" _").Append(field.Name).Append(";\n\n");
                        return;
                    }

                    members.Append(offset).Append("    public ").Append(type).Append(" ").Append(name).Append(";\n\n");
                    return;
                }

                case FieldKind.Handle:
                    members.Append(offset).Append("    public ").Append(Managed(field.Type)).Append(" ").Append(name).Append(";\n\n");
                    return;

                case FieldKind.String:
                    members.Append(offset).Append("    private sbyte* _").Append(field.Name).Append(";\n\n");
                    members.Append("    public string ").Append(name).Append(" => InteropHelpers.ToManagedString(_").Append(field.Name).Append(");\n\n");
                    return;

                case FieldKind.UserData:
                    members.Append(offset).Append("    public IntPtr ").Append(name).Append(";\n\n");
                    return;

                case FieldKind.Array:
                {
                    members.Append(offset).Append("    private void* _").Append(field.Name).Append(";\n\n");
                    string element = Managed(field.Element);
                    if (field.CountField == null || element == null || !IsBlittableManaged(field.Element))
                    {
                        Skip(owner.NativeName + "." + field.Name + ": array without a usable count or element type");
                        return;
                    }

                    FieldInfo count = owner.Fields.FirstOrDefault(f => f.Name == field.CountField);
                    string countExpression = "(int)" + Escape(field.CountField);
                    if (count == null)
                    {
                        Skip(owner.NativeName + "." + field.Name + ": count field " + field.CountField + " not found");
                        return;
                    }

                    string spanType = IsConstPointer(field) ? "ReadOnlySpan<" : "Span<";
                    members.Append("    public ").Append(spanType).Append(element).Append("> ").Append(name).Append(" => _").Append(field.Name)
                        .Append(" == null ? default : new ").Append(spanType).Append(element).Append(">(_").Append(field.Name).Append(", ").Append(countExpression).Append(");\n\n");
                    return;
                }

                case FieldKind.SinglePointer:
                {
                    members.Append(offset).Append("    private void* _").Append(field.Name).Append(";\n\n");
                    string element = Managed(field.Element);
                    if (element == null || !IsBlittableManaged(field.Element))
                    {
                        Skip(owner.NativeName + "." + field.Name + ": pointer to unsupported type");
                        return;
                    }

                    members.Append("    public ").Append(element).Append(" ").Append(name).Append(" => _").Append(field.Name)
                        .Append(" == null ? default : *(").Append(element).Append("*)_").Append(field.Name).Append(";\n\n");
                    return;
                }

                case FieldKind.FixedBuffer:
                {
                    string element = Managed(field.Element);
                    string bufferType = Pascal(field.Name) + "Array";
                    if (element == null || !IsBlittableManaged(field.Element))
                    {
                        Skip(owner.NativeName + "." + field.Name + ": fixed buffer of unsupported type");
                        return;
                    }

                    if (field.CountField == null)
                    {
                        nested.Append("    [InlineArray(").Append(field.FixedLength).Append(")]\n");
                        nested.Append("    public struct ").Append(bufferType).Append("\n    {\n        private ").Append(element).Append(" _element0;\n    }\n\n");
                        members.Append(offset).Append("    public ").Append(bufferType).Append(" ").Append(name).Append(";\n\n");
                        return;
                    }

                    nested.Append("    [InlineArray(").Append(field.FixedLength).Append(")]\n");
                    nested.Append("    private struct ").Append(bufferType).Append("\n    {\n        private ").Append(element).Append(" _element0;\n    }\n\n");
                    members.Append(offset).Append("    private ").Append(bufferType).Append(" _").Append(field.Name).Append(";\n\n");
                    members.Append("    [System.Diagnostics.CodeAnalysis.UnscopedRef]\n");
                    members.Append("    public Span<").Append(element).Append("> ").Append(name).Append(" => ((Span<").Append(element).Append(">)_").Append(field.Name)
                        .Append(").Slice(0, ").Append(Escape(field.CountField)).Append(");\n\n");
                    return;
                }

                case FieldKind.NestedStruct:
                {
                    string nestedType = Pascal(field.Name) + "Data";
                    if (!EmitBitfieldStruct(nested, nestedType, (INamedTypeSymbol)field.Type))
                    {
                        Skip(owner.NativeName + "." + field.Name + ": nested struct is not a single bitfield");
                        members.Append(offset).Append("    private ").Append(Full(field.Type)).Append(" _").Append(field.Name).Append(";\n\n");
                        return;
                    }

                    members.Append(offset).Append("    public ").Append(nestedType).Append(" ").Append(name).Append(";\n\n");
                    return;
                }

                case FieldKind.Union:
                    foreach (FieldInfo member in field.UnionMembers)
                    {
                        EmitPlainField(members, nested, owner, member);
                    }

                    return;

                case FieldKind.FunctionPointer:
                case FieldKind.Context:
                    members.Append(offset).Append("    private void* _").Append(field.Name).Append(";\n\n");
                    return;

                default:
                {
                    string type = field.Type is IPointerTypeSymbol || field.Type is IFunctionPointerTypeSymbol ? "void*" : Full(field.Type);
                    members.Append(offset).Append("    private ").Append(type).Append(" _").Append(field.Name).Append(";\n\n");
                    return;
                }
            }
        }

        private bool EmitBitfieldStruct(StringBuilder sb, string name, INamedTypeSymbol type)
        {
            List<IFieldSymbol> fields = NativeModel.InstanceFields(type).ToList();
            if (fields.Count != 1 || !NativeModel.IsInteger(fields[0].Type))
            {
                return false;
            }

            string backingType = fields[0].Type.ToDisplayString();
            sb.Append("    public struct ").Append(name).Append("\n    {\n        private ").Append(backingType).Append(" _bits;\n");
            int shift = 0;
            foreach (IPropertySymbol property in type.GetMembers().OfType<IPropertySymbol>())
            {
                string native = NativeModel.NativeTypeNameOf(property) ?? string.Empty;
                int colon = native.LastIndexOf(':');
                if (colon < 0 || !int.TryParse(native.Substring(colon + 1).Trim(), out int width))
                {
                    continue;
                }

                string propertyType = property.Type.ToDisplayString();
                string mask = "0x" + ((1UL << width) - 1).ToString("X") + (backingType == "ulong" ? "UL" : "u");
                sb.Append("\n        public ").Append(propertyType).Append(" ").Append(Escape(property.Name)).Append("\n        {\n");
                sb.Append("            readonly get => (").Append(propertyType).Append(")((_bits >> ").Append(shift).Append(") & ").Append(mask).Append(");\n");
                sb.Append("            set => _bits = (").Append(backingType).Append(")((_bits & ~(").Append(mask).Append(" << ").Append(shift)
                    .Append(")) | (((").Append(backingType).Append(")value & ").Append(mask).Append(") << ").Append(shift).Append("));\n");
                sb.Append("        }\n");
                shift += width;
            }

            sb.Append("    }\n\n");
            return true;
        }

        private void EmitManagedStruct(StringBuilder sb, StructInfo info)
        {
            var fields = new StringBuilder();
            var from = new StringBuilder();
            var to = new StringBuilder();
            string raw = Full(info.Symbol);
            foreach (FieldInfo field in info.Fields)
            {
                string name = Escape(field.Name);
                switch (field.Kind)
                {
                    case FieldKind.Value:
                    {
                        string type = Managed(field.Type);
                        if (type == null)
                        {
                            Skip(info.NativeName + "." + field.Name + ": unsupported field type " + field.Type.ToDisplayString());
                            break;
                        }

                        fields.Append("    public ").Append(type).Append(" ").Append(name).Append(";\n\n");
                        from.Append("        result.").Append(name).Append(" = ").Append(FromNative(field.Type, "raw." + name)).Append(";\n");
                        if (_model.CategoryOf(field.Type) == TypeCategory.Managed)
                        {
                            to.Append("        ").Append(name).Append(".ToNative(ref raw.").Append(name).Append(");\n");
                        }
                        else
                        {
                            to.Append("        raw.").Append(name).Append(" = ").Append(ToNative(field.Type, name)).Append(";\n");
                        }

                        break;
                    }

                    case FieldKind.Handle:
                        fields.Append("    public ").Append(Managed(field.Type)).Append(" ").Append(name).Append(";\n\n");
                        from.Append("        result.").Append(name).Append(" = ").Append(FromNative(field.Type, "raw." + name)).Append(";\n");
                        to.Append("        raw.").Append(name).Append(" = ").Append(name).Append(".Pointer;\n");
                        break;

                    case FieldKind.String:
                        fields.Append("    public string ").Append(name).Append(";\n\n");
                        from.Append("        result.").Append(name).Append(" = InteropHelpers.ToManagedString(raw.").Append(name).Append(");\n");
                        break;

                    case FieldKind.UserData:
                        fields.Append("    public IntPtr ").Append(name).Append(";\n\n");
                        from.Append("        result.").Append(name).Append(" = (IntPtr)raw.").Append(name).Append(";\n");
                        to.Append("        raw.").Append(name).Append(" = (void*)").Append(name).Append(";\n");
                        break;

                    case FieldKind.Array:
                    {
                        string element = Managed(field.Element);
                        if (element == null)
                        {
                            Skip(info.NativeName + "." + field.Name + ": array of unsupported element type");
                            break;
                        }

                        string container = IsSpanStruct(info) ? "Span<" : "Memory<";
                        fields.Append("    public ").Append(IsConstPointer(field) ? "ReadOnly" : string.Empty).Append(container).Append(element).Append("> ").Append(name).Append(";\n\n");
                        if (field.CountField != null && IsBlittableManaged(field.Element))
                        {
                            string count = "raw." + Escape(field.CountField) + (field.CountDivisor != 1 ? " * " + field.CountDivisor : string.Empty);
                            from.Append("        result.").Append(name).Append(" = InteropHelpers.ToArray<").Append(element).Append(">(raw.")
                                .Append(name).Append(", (int)(").Append(count).Append("));\n");
                        }

                        if (field.CountField != null)
                        {
                            to.Append("        raw.").Append(Escape(field.CountField)).Append(" = ").Append(name).Append(".Length")
                                .Append(field.CountDivisor != 1 ? " / " + field.CountDivisor : string.Empty).Append(";\n");
                        }

                        break;
                    }

                    case FieldKind.FixedBuffer:
                    case FieldKind.Union:
                        Skip(info.NativeName + "." + field.Name + ": fixed buffer or union in a managed struct");
                        break;
                }
            }

            sb.Append(IsSpanStruct(info) ? "public unsafe ref partial struct " : "public unsafe partial struct ").Append(info.ManagedName).Append("\n{\n");
            sb.Append(fields);
            if (info.DefaultFunction != null)
            {
                sb.Append("    public static ").Append(info.ManagedName).Append(" Default => FromNative(global::Box3D.Interop.Native.")
                    .Append(info.DefaultFunction.Name).Append("());\n\n");
            }

            sb.Append("    internal static unsafe ").Append(info.ManagedName).Append(IsSpanStruct(info) ? " FromNative(scoped in " : " FromNative(in ").Append(raw).Append(" raw)\n    {\n");
            sb.Append("        ").Append(info.ManagedName).Append(" result = default;\n");
            sb.Append(from);
            sb.Append("        return result;\n    }\n\n");
            sb.Append("    internal readonly unsafe void ToNative(ref ").Append(raw).Append(" raw)\n    {\n");
            sb.Append(to);
            sb.Append("    }\n}\n\n");
        }
    }
}
