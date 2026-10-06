using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Box3D.Sharp.Generator
{
    internal enum TypeCategory
    {
        Primitive,
        Numerics,
        Enum,
        Id,
        Handle,
        ValueHandle,
        Plain,
        Opaque,
        Managed,
        Unsupported,
    }

    internal enum FieldKind
    {
        Value,
        String,
        UserData,
        Array,
        Count,
        Handle,
        SinglePointer,
        FixedBuffer,
        Union,
        Hidden,
        InternalValue,
        NestedStruct,
        FunctionPointer,
        Context,
    }

    internal sealed class FieldInfo
    {
        public IFieldSymbol Symbol;
        public string Name;
        public int Offset;
        public FieldKind Kind;
        public ITypeSymbol Type;
        public ITypeSymbol Element;
        public string CountField;
        public int CountDivisor = 1;
        public int FixedLength;
        public List<FieldInfo> UnionMembers;
    }

    internal sealed class StructInfo
    {
        public INamedTypeSymbol Symbol;
        public string NativeName;
        public string ManagedName;
        public int Size;
        public int Align;
        public List<FieldInfo> Fields = new List<FieldInfo>();
        public bool IsInput;
        public IMethodSymbol DefaultFunction;
    }

    internal sealed class NativeModel
    {
        public const string InteropNamespace = "Box3D.Interop";

        private static readonly HashSet<string> s_valueHandles = new HashSet<string> { "b3DynamicTree" };

        private static readonly Dictionary<string, (string countField, int divisor)> s_arrayOverrides =
            new Dictionary<string, (string, int)>
            {
                { "b3MeshDef.indices", ("triangleCount", 3) },
                { "b3MeshDef.materialIndices", (null, 1) },
                { "b3HeightFieldDef.heights", (null, 1) },
                { "b3HeightFieldDef.materialIndices", (null, 1) },
            };

        private static readonly HashSet<string> s_hiddenFields = new HashSet<string> { "b3MeshDef.stride", "b3WorldDef.userData" };

        public readonly Compilation Compilation;
        public readonly INamedTypeSymbol Native;
        public readonly Dictionary<string, INamedTypeSymbol> Types = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        public readonly Dictionary<string, TypeCategory> Categories = new Dictionary<string, TypeCategory>(StringComparer.Ordinal);
        public readonly Dictionary<string, StructInfo> Structs = new Dictionary<string, StructInfo>(StringComparer.Ordinal);
        public readonly List<IMethodSymbol> Functions = new List<IMethodSymbol>();
        public readonly List<string> Messages = new List<string>();

        private readonly Dictionary<string, (int size, int align)> _layoutCache = new Dictionary<string, (int, int)>();

        public NativeModel(Compilation compilation)
        {
            Compilation = compilation;
            Native = compilation.GetTypeByMetadataName(InteropNamespace + ".Native");
            if (Native == null)
            {
                return;
            }

            foreach (INamedTypeSymbol type in Native.ContainingNamespace.GetTypeMembers())
            {
                if (type.Name.StartsWith("b3", StringComparison.Ordinal))
                {
                    Types[type.Name] = type;
                }
            }

            Functions.AddRange(Native.GetMembers().OfType<IMethodSymbol>()
                .Where(m => m.IsStatic && m.IsExtern && m.Name.StartsWith("b3", StringComparison.Ordinal)));

            Classify();
        }


        public static string NativeTypeNameOf(ISymbol symbol)
        {
            return ReadNativeTypeName(symbol.GetAttributes());
        }

        public static string NativeReturnTypeName(IMethodSymbol method)
        {
            return ReadNativeTypeName(method.GetReturnTypeAttributes());
        }

        private static string ReadNativeTypeName(IEnumerable<AttributeData> attributes)
        {
            foreach (AttributeData attribute in attributes)
            {
                if (attribute.AttributeClass?.Name == "NativeTypeNameAttribute" && attribute.ConstructorArguments.Length == 1)
                {
                    return attribute.ConstructorArguments[0].Value as string;
                }
            }

            return null;
        }

        public static string StripPrefix(string name)
        {
            return name.StartsWith("b3", StringComparison.Ordinal) ? name.Substring(2) : name;
        }

        private static readonly Dictionary<string, string> s_typeRenames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "b3RecPlayer", "RecordPlayer" },
            { "b3RecPlayerInfo", "RecordPlayerInfo" },
        };

        public string ManagedTypeName(string nativeName)
        {
            if (s_typeRenames.TryGetValue(nativeName, out string renamed))
            {
                return renamed;
            }

            string name = StripPrefix(nativeName);
            if (Categories.TryGetValue(nativeName, out TypeCategory category) && category == TypeCategory.Id && name.EndsWith("Id", StringComparison.Ordinal))
            {
                return name.Substring(0, name.Length - 2);
            }

            return name;
        }

        public TypeCategory CategoryOf(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_IntPtr:
                case SpecialType.System_UIntPtr:
                    return TypeCategory.Primitive;
            }

            if (type is INamedTypeSymbol named)
            {
                if (named.ContainingNamespace?.ToDisplayString() == "System.Numerics")
                {
                    return TypeCategory.Numerics;
                }

                if (named.TypeKind == TypeKind.Enum && Categories.TryGetValue(named.Name, out TypeCategory enumCategory))
                {
                    return enumCategory;
                }

                if (named.ContainingType == null && Categories.TryGetValue(named.Name, out TypeCategory category))
                {
                    return category;
                }
            }

            return TypeCategory.Unsupported;
        }

        public bool IsHandlePointer(ITypeSymbol type)
        {
            return type is IPointerTypeSymbol p && p.PointedAtType is INamedTypeSymbol n
                && Categories.TryGetValue(n.Name, out TypeCategory c) && (c == TypeCategory.Handle || c == TypeCategory.ValueHandle);
        }

        private void Classify()
        {
            foreach (INamedTypeSymbol type in Types.Values)
            {
                if (type.TypeKind == TypeKind.Enum)
                {
                    Categories[type.Name] = TypeCategory.Enum;
                }
                else if (type.TypeKind == TypeKind.Struct && type.Name.EndsWith("Id", StringComparison.Ordinal)
                    && InstanceFields(type).All(f => IsInteger(f.Type)))
                {
                    Categories[type.Name] = TypeCategory.Id;
                }
            }

            foreach (string name in s_valueHandles)
            {
                if (Types.ContainsKey(name))
                {
                    Categories[name] = TypeCategory.ValueHandle;
                }
            }

            foreach (IMethodSymbol function in Functions)
            {
                if (function.ReturnType is IPointerTypeSymbol p && p.PointedAtType is INamedTypeSymbol n
                    && Types.ContainsKey(n.Name) && !Categories.ContainsKey(n.Name) && n.Name != "b3SurfaceMaterial")
                {
                    Categories[n.Name] = TypeCategory.Handle;
                }
            }

            foreach (INamedTypeSymbol type in Types.Values)
            {
                if (type.TypeKind == TypeKind.Struct && !Categories.ContainsKey(type.Name) && !InstanceFields(type).Any())
                {
                    Categories[type.Name] = TypeCategory.Handle;
                }
            }

            var structs = Types.Values.Where(t => t.TypeKind == TypeKind.Struct && !Categories.ContainsKey(t.Name)).ToList();
            foreach (INamedTypeSymbol type in structs)
            {
                Categories[type.Name] = TypeCategory.Plain;
            }

            foreach (INamedTypeSymbol type in structs)
            {
                var info = new StructInfo
                {
                    Symbol = type,
                    NativeName = type.Name,
                    ManagedName = ManagedTypeName(type.Name),
                };
                (info.Size, info.Align) = Layout(type);
                Structs[type.Name] = info;
            }

            foreach (IMethodSymbol function in Functions)
            {
                if (function.Name.StartsWith("b3Default", StringComparison.Ordinal) && function.Parameters.Length == 0
                    && function.ReturnType is INamedTypeSymbol rt && Structs.TryGetValue(rt.Name, out StructInfo defaulted))
                {
                    defaulted.DefaultFunction = function;
                    defaulted.IsInput = true;
                }

                foreach (IParameterSymbol parameter in function.Parameters)
                {
                    ITypeSymbol t = parameter.Type;
                    string nativeName = NativeTypeNameOf(parameter) ?? string.Empty;
                    if (t is IPointerTypeSymbol pointer && nativeName.StartsWith("const ", StringComparison.Ordinal))
                    {
                        t = pointer.PointedAtType;
                    }

                    if (t is INamedTypeSymbol named && Structs.TryGetValue(named.Name, out StructInfo input))
                    {
                        input.IsInput = true;
                    }
                }
            }

            foreach (StructInfo info in Structs.Values)
            {
                AnalyzeFields(info);
            }

            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (StructInfo info in Structs.Values)
                {
                    if (!info.IsInput)
                    {
                        continue;
                    }

                    foreach (FieldInfo field in info.Fields)
                    {
                        ITypeSymbol inner = field.Kind == FieldKind.Value ? field.Type : field.Kind == FieldKind.Array ? field.Element : null;
                        if (inner is INamedTypeSymbol n && Structs.TryGetValue(n.Name, out StructInfo nested) && !nested.IsInput)
                        {
                            nested.IsInput = true;
                            changed = true;
                        }
                    }
                }
            }

            foreach (StructInfo info in Structs.Values.Where(s => s.IsInput))
            {
                foreach (FieldInfo field in info.Fields.Where(f => f.Kind == FieldKind.Array && f.CountField != null))
                {
                    FieldInfo count = info.Fields.FirstOrDefault(f => f.Name == field.CountField);
                    if (count != null)
                    {
                        count.Kind = FieldKind.Count;
                    }
                }
            }

            foreach (StructInfo info in Structs.Values)
            {
                if (info.Fields.Any(f => f.Kind == FieldKind.Value && f.Type is INamedTypeSymbol n && Categories.TryGetValue(n.Name, out TypeCategory c) && c == TypeCategory.Handle))
                {
                    Categories[info.NativeName] = TypeCategory.Opaque;
                }
            }

            changed = true;
            while (changed)
            {
                changed = false;
                foreach (StructInfo info in Structs.Values)
                {
                    if (Categories[info.NativeName] != TypeCategory.Plain)
                    {
                        continue;
                    }

                    bool managed = false;
                    foreach (FieldInfo field in info.Fields)
                    {
                        if (info.IsInput && (field.Kind == FieldKind.String || field.Kind == FieldKind.Array || field.Kind == FieldKind.Hidden || field.Kind == FieldKind.SinglePointer))
                        {
                            managed = true;
                        }

                        if (field.Kind == FieldKind.Value && field.Type is INamedTypeSymbol n && Categories.TryGetValue(n.Name, out TypeCategory c) && c == TypeCategory.Managed)
                        {
                            managed = true;
                        }
                    }

                    if (managed)
                    {
                        Categories[info.NativeName] = TypeCategory.Managed;
                        changed = true;
                    }
                }
            }
        }

        private void AnalyzeFields(StructInfo info)
        {
            List<(IFieldSymbol field, int offset)> fields = FieldOffsets(info.Symbol);
            foreach ((IFieldSymbol symbol, int offset) in fields)
            {
                info.Fields.Add(AnalyzeField(info, symbol, offset, fields));
            }

            foreach (FieldInfo field in info.Fields)
            {
                if (field.Kind == FieldKind.Array && field.CountField != null)
                {
                    FieldInfo count = info.Fields.FirstOrDefault(f => f.Name == field.CountField);
                    if (count != null && info.IsInput)
                    {
                        count.Kind = FieldKind.Count;
                    }
                }
            }
        }

        private FieldInfo AnalyzeField(StructInfo owner, IFieldSymbol symbol, int offset, List<(IFieldSymbol field, int offset)> siblings)
        {
            var field = new FieldInfo { Symbol = symbol, Name = symbol.Name, Offset = offset, Type = symbol.Type };
            string nativeName = NativeTypeNameOf(symbol) ?? string.Empty;
            string key = owner.NativeName + "." + symbol.Name;

            if (symbol.Name == "internalValue")
            {
                field.Kind = FieldKind.InternalValue;
                return field;
            }

            if (s_hiddenFields.Contains(key))
            {
                field.Kind = FieldKind.Hidden;
                return field;
            }

            ITypeSymbol type = symbol.Type;
            if (type is IFunctionPointerTypeSymbol)
            {
                field.Kind = FieldKind.FunctionPointer;
                return field;
            }

            if (type is IPointerTypeSymbol pointer)
            {
                ITypeSymbol pointee = pointer.PointedAtType;
                if (pointee.SpecialType == SpecialType.System_SByte && nativeName.Contains("char"))
                {
                    field.Kind = FieldKind.String;
                    return field;
                }

                if (pointee.SpecialType == SpecialType.System_Void)
                {
                    field.Kind = symbol.Name == "userData" ? FieldKind.UserData : FieldKind.Context;
                    return field;
                }

                if (IsHandlePointer(type))
                {
                    field.Kind = FieldKind.Handle;
                    return field;
                }

                field.Element = pointee;
                if (s_arrayOverrides.TryGetValue(key, out (string countField, int divisor) over))
                {
                    field.Kind = FieldKind.Array;
                    field.CountField = over.countField;
                    field.CountDivisor = over.divisor;
                    return field;
                }

                string count = FindCountField(symbol.Name, siblings);
                if (count != null)
                {
                    field.Kind = FieldKind.Array;
                    field.CountField = count;
                    return field;
                }

                if (pointee is INamedTypeSymbol pn && Structs.ContainsKey(pn.Name))
                {
                    field.Kind = FieldKind.SinglePointer;
                    return field;
                }

                field.Kind = FieldKind.Hidden;
                return field;
            }

            if (type is INamedTypeSymbol named && named.ContainingType != null)
            {
                if (named.Name.EndsWith("_e__Struct", StringComparison.Ordinal))
                {
                    field.Kind = FieldKind.NestedStruct;
                    return field;
                }

                int length = InlineArrayLength(named);
                if (length > 0)
                {
                    field.Kind = FieldKind.FixedBuffer;
                    field.FixedLength = length;
                    field.Element = InstanceFields(named).First().Type;
                    field.CountField = FindCountField(symbol.Name, siblings)
                        ?? (siblings.Any(s => s.field.Name == "count" && IsInteger(s.field.Type)) ? "count" : null);
                    return field;
                }

                if (IsExplicit(named))
                {
                    field.Kind = FieldKind.Union;
                    field.UnionMembers = new List<FieldInfo>();
                    List<(IFieldSymbol field, int offset)> members = FieldOffsets(named);
                    foreach ((IFieldSymbol member, int memberOffset) in members)
                    {
                        field.UnionMembers.Add(AnalyzeField(owner, member, offset + memberOffset, members));
                    }

                    return field;
                }
            }

            field.Kind = FieldKind.Value;
            return field;
        }

        private static string FindCountField(string pointerName, List<(IFieldSymbol field, int offset)> siblings)
        {
            var candidates = new List<string> { pointerName + "Count" };
            string singular = Singular(pointerName);
            candidates.Add(singular + "Count");
            int lastUpper = LastWordStart(pointerName);
            if (lastUpper > 0)
            {
                candidates.Add(pointerName.Substring(0, lastUpper) + "Count");
            }

            int lastUpperSingular = LastWordStart(singular);
            if (lastUpperSingular > 0)
            {
                candidates.Add(singular.Substring(0, lastUpperSingular) + "Count");
            }

            foreach (string candidate in candidates)
            {
                if (siblings.Any(s => s.field.Name == candidate && IsInteger(s.field.Type)))
                {
                    return candidate;
                }
            }

            int pointerCount = siblings.Count(s => s.field.Type is IPointerTypeSymbol);
            if (pointerCount == 1 && siblings.Any(s => s.field.Name == "count" && IsInteger(s.field.Type)))
            {
                return "count";
            }

            return null;
        }

        private static string Singular(string name)
        {
            if (name.EndsWith("ices", StringComparison.Ordinal))
            {
                return name.Substring(0, name.Length - 4) + "ex";
            }

            if (name.EndsWith("shes", StringComparison.Ordinal))
            {
                return name.Substring(0, name.Length - 2);
            }

            if (name.EndsWith("s", StringComparison.Ordinal))
            {
                return name.Substring(0, name.Length - 1);
            }

            return name;
        }

        private static int LastWordStart(string name)
        {
            for (int i = name.Length - 1; i > 0; --i)
            {
                if (char.IsUpper(name[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        public static bool IsInteger(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                    return true;
                default:
                    return false;
            }
        }

        public static IEnumerable<IFieldSymbol> InstanceFields(INamedTypeSymbol type)
        {
            return type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst);
        }

        public static int InlineArrayLength(INamedTypeSymbol type)
        {
            foreach (AttributeData attribute in type.GetAttributes())
            {
                if (attribute.AttributeClass?.Name == "InlineArrayAttribute" && attribute.ConstructorArguments.Length == 1)
                {
                    return (int)attribute.ConstructorArguments[0].Value;
                }
            }

            return 0;
        }

        private static bool IsExplicit(INamedTypeSymbol type)
        {
            return type.ContainingType != null && type.Name.EndsWith("_e__Union", StringComparison.Ordinal);
        }

        public List<(IFieldSymbol field, int offset)> FieldOffsets(INamedTypeSymbol type)
        {
            var result = new List<(IFieldSymbol, int)>();
            bool isExplicit = IsExplicit(type);
            int offset = 0;
            foreach (IFieldSymbol field in InstanceFields(type))
            {
                (int size, int align) = Layout(field.Type);
                if (isExplicit)
                {
                    result.Add((field, 0));
                    continue;
                }

                offset = Align(offset, align);
                result.Add((field, offset));
                offset += size;
            }

            return result;
        }

        public static int Align(int value, int align)
        {
            return (value + align - 1) / align * align;
        }

        public (int size, int align) Layout(ITypeSymbol type)
        {
            if (type is IPointerTypeSymbol || type is IFunctionPointerTypeSymbol)
            {
                return (8, 8);
            }

            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                    return (1, 1);
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Char:
                    return (2, 2);
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Single:
                    return (4, 4);
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Double:
                case SpecialType.System_IntPtr:
                case SpecialType.System_UIntPtr:
                    return (8, 8);
            }

            var named = (INamedTypeSymbol)type;
            if (named.TypeKind == TypeKind.Enum)
            {
                return Layout(named.EnumUnderlyingType);
            }

            if (named.ContainingNamespace?.ToDisplayString() == "System.Numerics")
            {
                switch (named.Name)
                {
                    case "Vector2": return (8, 4);
                    case "Vector3": return (12, 4);
                    case "Vector4":
                    case "Quaternion": return (16, 4);
                }
            }

            string key = named.ToDisplayString();
            if (_layoutCache.TryGetValue(key, out (int, int) cached))
            {
                return cached;
            }

            int length = InlineArrayLength(named);
            (int size, int align) result;
            if (length > 0)
            {
                (int elementSize, int elementAlign) = Layout(InstanceFields(named).First().Type);
                result = (elementSize * length, elementAlign);
            }
            else
            {
                int end = 0;
                int align = 1;
                foreach ((IFieldSymbol field, int offset) in FieldOffsets(named))
                {
                    (int fieldSize, int fieldAlign) = Layout(field.Type);
                    end = Math.Max(end, offset + fieldSize);
                    align = Math.Max(align, fieldAlign);
                }

                result = (Align(Math.Max(end, 1), align), align);
            }

            _layoutCache[key] = result;
            return result;
        }
    }

}
