using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Box3D.Sharp.Generator
{
    internal sealed class Target
    {
        public string Key;
        public string ManagedType;
        public string ReceiverName;
        public string Word;
        public string ExtensionClass;
        public ITypeSymbol NativeType;
        public bool ByReference;
        public bool IsClass;
        public bool IsJointBase;
        public readonly List<Plan> Members = new List<Plan>();
        public readonly List<Plan> Statics = new List<Plan>();
    }

    internal sealed class Plan
    {
        public IMethodSymbol Function;
        public Target Target;
        public string Name;
        public readonly List<string> Parameters = new List<string>();
        public readonly List<string> ParameterTypes = new List<string>();
        public readonly List<string> TypeParameters = new List<string>();
        public readonly List<string> Constraints = new List<string>();
        public readonly List<string> Pre = new List<string>();
        public readonly List<string> Post = new List<string>();
        public readonly List<string> Arguments = new List<string>();
        public string ReturnType = "void";
        public string ReturnExpression;
        public int Pins;
        public bool IsCollide;

        public Plan Clone()
        {
            var copy = new Plan
            {
                Function = Function,
                Target = Target,
                Name = Name,
                ReturnType = ReturnType,
                ReturnExpression = ReturnExpression,
                Pins = Pins,
                IsCollide = IsCollide,
            };
            copy.Parameters.AddRange(Parameters);
            copy.ParameterTypes.AddRange(ParameterTypes);
            copy.TypeParameters.AddRange(TypeParameters);
            copy.Constraints.AddRange(Constraints);
            copy.Pre.AddRange(Pre);
            copy.Post.AddRange(Post);
            copy.Arguments.AddRange(Arguments);
            return copy;
        }
    }
    internal sealed class HandlerInfo
    {
        public string Interface;
        public string Method;
        public string Thunk;
        public string Dispatch;
        public IFunctionPointerTypeSymbol Pointer;
        public string ManagedReturn;
        public readonly List<string> ManagedParameters = new List<string>();
        public readonly List<string> ManagedTypes = new List<string>();
        public readonly List<string> ThunkParameters = new List<string>();
        public readonly List<string> Conversions = new List<string>();
    }

    internal sealed partial class Emitter
    {
        private readonly Target _static = new Target { Key = "B3", ManagedType = "B3", ExtensionClass = "B3" };

        private Target TargetFor(string nativeTypeName, string typedJoint)
        {
            string key = typedJoint ?? nativeTypeName;
            if (_targets.TryGetValue(key, out Target existing))
            {
                return existing;
            }

            INamedTypeSymbol native = _model.Types[nativeTypeName];
            TypeCategory category = _model.Categories[nativeTypeName];
            string managed = typedJoint ?? _model.ManagedTypeName(nativeTypeName);
            string word = managed;
            if (word.EndsWith("Id", StringComparison.Ordinal))
            {
                word = word.Substring(0, word.Length - 2);
            }
            else if (word.EndsWith("Data", StringComparison.Ordinal))
            {
                word = word.Substring(0, word.Length - 4);
            }

            string receiver = category == TypeCategory.ValueHandle ? "tree" : typedJoint != null ? "joint" : Camel(word);
            if (category == TypeCategory.Handle && managed.EndsWith("Data", StringComparison.Ordinal))
            {
                receiver = Camel(word);
            }

            var target = new Target
            {
                Key = key,
                ManagedType = managed,
                ReceiverName = Escape(receiver),
                Word = typedJoint ?? word,
                ExtensionClass = managed + "Extensions",
                NativeType = native,
                ByReference = category == TypeCategory.Plain,
                IsClass = category == TypeCategory.ValueHandle,
                IsJointBase = typedJoint == null && nativeTypeName == "b3JointId",
            };
            _targets[key] = target;
            return target;
        }

        private string ReceiverArgument(Target target, bool byPointer, List<string> pre)
        {
            TypeCategory category = _model.Categories[target.NativeType.Name];
            string r = target.ReceiverName;
            switch (category)
            {
                case TypeCategory.Id:
                    return target.ManagedType != _model.ManagedTypeName(target.NativeType.Name)
                        ? "Unsafe.BitCast<" + target.ManagedType + ", " + Full(target.NativeType) + ">(" + r + ")"
                        : ToNative(target.NativeType, r);
                case TypeCategory.Handle:
                case TypeCategory.ValueHandle:
                    return r + ".Pointer";
                case TypeCategory.Plain:
                    if (byPointer)
                    {
                        pre.Add("fixed (" + target.ManagedType + "* __self = &Unsafe.AsRef(in " + r + "))");
                        return "(" + Full(target.NativeType) + "*)__self";
                    }

                    return ToNative(target.NativeType, r);
                default:
                    return r;
            }
        }

        private static string RemoveWord(string name, string word)
        {
            if (string.IsNullOrEmpty(word))
            {
                return name;
            }

            for (int i = 1; i < word.Length; ++i)
            {
                string result = RemoveExactWord(name, word);
                if (!ReferenceEquals(result, name))
                {
                    return result;
                }

                while (i < word.Length && !char.IsUpper(word[i]))
                {
                    ++i;
                }

                if (i >= word.Length)
                {
                    break;
                }

                word = word.Substring(i);
                i = 0;
            }

            return RemoveExactWord(name, word);
        }

        private static string RemoveExactWord(string name, string word)
        {

            int index = 0;
            while ((index = name.IndexOf(word, index, StringComparison.Ordinal)) >= 0)
            {
                int end = index + word.Length;
                if (end == name.Length || char.IsUpper(name[end]) || char.IsDigit(name[end]))
                {
                    string result = name.Remove(index, word.Length);
                    if (result.Length > 0 && char.IsUpper(result[0]))
                    {
                        return result;
                    }

                    return name;
                }

                index = end;
            }

            return name;
        }

        private void EmitFunctions()
        {
            foreach (string joint in _typedJoints)
            {
                TargetFor("b3JointId", joint);
            }

            foreach (IMethodSymbol function in _model.Functions)
            {
                if (s_excludedFunctions.Contains(function.Name))
                {
                    continue;
                }

                if (function.Name.StartsWith("b3Default", StringComparison.Ordinal) && function.Parameters.Length == 0
                    && function.ReturnType is INamedTypeSymbol rt && _model.Structs.ContainsKey(rt.Name))
                {
                    continue;
                }

                PlanFunction(function);
            }

            var sb = new StringBuilder();
            foreach (Target target in _targets.Values.OrderBy(t => t.Key, StringComparer.Ordinal))
            {
                EmitTarget(sb, target, target);
                if (target.IsJointBase)
                {
                    foreach (string joint in _typedJoints)
                    {
                        Target typed = TargetFor("b3JointId", joint);
                        EmitTarget(sb, typed, target);
                    }
                }
            }

            EmitStaticClass(sb);
            AddFile("Functions", sb.ToString());
        }

        private void PlanFunction(IMethodSymbol function)
        {
            string name = function.Name.Substring(2);
            Target target = null;
            string memberName = name;
            ImmutableParameters parameters = new ImmutableParameters(function.Parameters);
            bool staticOnTarget = false;

            int underscore = name.IndexOf('_');
            if (underscore > 0)
            {
                string prefix = name.Substring(0, underscore);
                memberName = name.Substring(underscore + 1);
                target = ResolvePrefixTarget(prefix);
                if (target != null && (parameters.Count == 0 || !MatchesReceiver(target, parameters[0].Type)))
                {
                    staticOnTarget = true;
                }
            }
            else if (IsCreator(name) && CreationTarget(function) is Target created)
            {
                target = created;
                staticOnTarget = true;
                memberName = RemoveWord(name, created.Word);
            }
            else if (parameters.Count > 0)
            {
                target = ResolveFirstParameterTarget(parameters[0].Type, parameters[0]);
                if (target != null)
                {
                    memberName = RemoveWord(name, target.Word);
                }
            }

            if (target != null && target.ByReference == false && _model.Categories[target.NativeType.Name] == TypeCategory.Plain)
            {
                target.ByReference = true;
            }

            if (!staticOnTarget && target != null && memberName == "IsValid" && _model.Categories[target.NativeType.Name] == TypeCategory.Id)
            {
                memberName = "Exists";
            }

            var plan = new Plan { Function = function, Target = staticOnTarget ? null : target, Name = memberName };
            int start = 0;
            if (plan.Target != null)
            {
                bool byPointer = parameters[0].Type is IPointerTypeSymbol;
                plan.Arguments.Add(ReceiverArgument(target, byPointer, plan.Pre));
                start = 1;
            }

            List<Plan> variants = MapParameters(plan, function, start);
            if (variants == null)
            {
                return;
            }

            foreach (Plan variant in variants)
            {
                if (!MapReturn(variant, function))
                {
                    return;
                }

                if (variant.Target != null)
                {
                    variant.Target.Members.Add(variant);
                }
                else if (staticOnTarget && target != null)
                {
                    target.Statics.Add(variant);
                }
                else
                {
                    _static.Members.Add(variant);
                }
            }
        }

        private static bool IsCreator(string name)
        {
            return name.StartsWith("Create", StringComparison.Ordinal) || name.StartsWith("Make", StringComparison.Ordinal)
                || name.StartsWith("Load", StringComparison.Ordinal);
        }

        private Target CreationTarget(IMethodSymbol function)
        {
            ITypeSymbol type = function.ReturnType;
            if (type is IPointerTypeSymbol pointer)
            {
                type = pointer.PointedAtType;
                return _model.Categories.TryGetValue(type.Name, out TypeCategory handle) && handle == TypeCategory.Handle ? TargetFor(type.Name, null) : null;
            }

            if (!_model.Categories.TryGetValue(type.Name, out TypeCategory category) || !(type is INamedTypeSymbol named) || named.ContainingType != null)
            {
                return null;
            }

            if (type.Name == "b3JointId")
            {
                string joint = function.Name.StartsWith("b3Create", StringComparison.Ordinal) ? function.Name.Substring("b3Create".Length) : null;
                return joint != null && _typedJoints.Contains(joint) ? TargetFor("b3JointId", joint) : null;
            }

            switch (category)
            {
                case TypeCategory.Id:
                case TypeCategory.ValueHandle:
                case TypeCategory.Plain:
                case TypeCategory.Opaque:
                    return TargetFor(type.Name, null);
                default:
                    return null;
            }
        }

        private Target ResolvePrefixTarget(string prefix)
        {
            if (_model.Types.ContainsKey("b3" + prefix + "Id"))
            {
                return TargetFor("b3" + prefix + "Id", null);
            }

            if (_typedJoints.Contains(prefix))
            {
                return TargetFor("b3JointId", prefix);
            }

            foreach (string candidate in new[] { "b3" + prefix, "b3" + prefix + "Data" })
            {
                if (_model.Categories.TryGetValue(candidate, out TypeCategory category) && (category == TypeCategory.Handle || category == TypeCategory.ValueHandle))
                {
                    return TargetFor(candidate, null);
                }
            }

            return null;
        }

        private Target ResolveFirstParameterTarget(ITypeSymbol type, IParameterSymbol parameter)
        {
            ITypeSymbol pointee = type is IPointerTypeSymbol p ? p.PointedAtType : null;
            if (pointee != null)
            {
                if (!_model.Categories.TryGetValue(pointee.Name, out TypeCategory pc))
                {
                    return null;
                }

                if (pc == TypeCategory.Handle || pc == TypeCategory.ValueHandle)
                {
                    return TargetFor(pointee.Name, null);
                }

                string nativeName = NativeModel.NativeTypeNameOf(parameter) ?? string.Empty;
                if (pc == TypeCategory.Plain && nativeName.StartsWith("const ", StringComparison.Ordinal) && pointee.Name != "b3LocalManifold")
                {
                    return TargetFor(pointee.Name, null);
                }

                return null;
            }

            if (_model.Categories.TryGetValue(type.Name, out TypeCategory category) && type is INamedTypeSymbol named && named.ContainingType == null)
            {
                if (category == TypeCategory.Id || category == TypeCategory.Plain)
                {
                    return TargetFor(type.Name, null);
                }
            }

            return null;
        }

        private bool MatchesReceiver(Target target, ITypeSymbol type)
        {
            ITypeSymbol t = type is IPointerTypeSymbol p ? p.PointedAtType : type;
            return SymbolEqualityComparer.Default.Equals(t, target.NativeType);
        }

        private List<Plan> MapParameters(Plan plan, IMethodSymbol function, int start)
        {
            var plans = new List<Plan> { plan };
            ImmutableParameters parameters = new ImmutableParameters(function.Parameters);
            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
            if (plan.Target != null)
            {
                used.Add(plan.Target.ReceiverName);
            }

            for (int i = start; i < parameters.Count; ++i)
            {
                IParameterSymbol parameter = parameters[i];
                ITypeSymbol type = parameter.Type;
                string nativeName = NativeModel.NativeTypeNameOf(parameter) ?? string.Empty;
                bool isConst = nativeName.StartsWith("const ", StringComparison.Ordinal);
                string parameterName = parameter.Name;
                if (_model.CategoryOf(type) == TypeCategory.Id && parameterName.EndsWith("Id", StringComparison.Ordinal) && parameterName.Length > 2)
                {
                    parameterName = parameterName.Substring(0, parameterName.Length - 2);
                }

                string name = Escape(parameterName);
                if (used.Contains(name))
                {
                    name = Escape(parameter.Name + "Value");
                }

                used.Add(name);
                IParameterSymbol next = i + 1 < parameters.Count ? parameters[i + 1] : null;

                if (type is IFunctionPointerTypeSymbol fnptr && next != null && next.Type is IPointerTypeSymbol contextPointer
                    && contextPointer.PointedAtType.SpecialType == SpecialType.System_Void && !IsPersistentCallback(function))
                {
                    HandlerInfo handler = Handler(nativeName, fnptr, function);
                    if (handler == null)
                    {
                        return null;
                    }

                    foreach (Plan p in plans)
                    {
                        string typeParameter = "T" + handler.Interface.Substring(1);
                        string state = "__" + parameter.Name + "State";
                        string context = "__" + parameter.Name + "Context";
                        p.TypeParameters.Add(typeParameter);
                        p.Constraints.Add("where " + typeParameter + " : struct, " + handler.Interface);
                        p.Parameters.Add("ref " + typeParameter + " handler");
                        p.ParameterTypes.Add("ref " + typeParameter);
                        p.Pre.Add(typeParameter + " " + state + " = handler;");
                        p.Pre.Add("CallbackContext " + context + " = new CallbackContext((void*)(delegate*<void*, "
                            + string.Join(", ", handler.ManagedTypes.Concat(new[] { handler.ManagedReturn })) + ">)&Callbacks."
                            + handler.Dispatch + "<" + typeParameter + ">, Unsafe.AsPointer(ref " + state + "));");
                        p.Arguments.Add("&Callbacks." + handler.Thunk);
                        p.Arguments.Add("&" + context);
                        p.Post.Add("handler = " + state + ";");
                        p.Post.Add(context + ".ThrowIfFailed();");
                    }

                    ++i;
                    continue;
                }

                if (type is IFunctionPointerTypeSymbol rawPointer)
                {
                    string callbackType = FunctionWrapper(rawPointer, nativeName, Pascal(parameter.Name));
                    foreach (Plan p in plans)
                    {
                        p.Parameters.Add(callbackType + " " + name);
                        p.ParameterTypes.Add(callbackType);
                        p.Arguments.Add("(" + Full(type) + ")(void*)" + name + ".Function");
                    }

                    continue;
                }

                if (type is IPointerTypeSymbol pointer)
                {
                    ITypeSymbol pointee = pointer.PointedAtType;
                    if (pointee.SpecialType == SpecialType.System_SByte && nativeName.Contains("char"))
                    {
                        foreach (Plan p in plans)
                        {
                            p.Parameters.Add("string " + name);
                            p.ParameterTypes.Add("string");
                            p.Arguments.Add(PinString(p, name));
                        }

                        continue;
                    }

                    if (pointee.SpecialType == SpecialType.System_Void && next != null && NativeModel.IsInteger(next.Type))
                    {
                        string byteSpan = isConst ? "ReadOnlySpan<byte>" : "Span<byte>";
                        foreach (Plan p in plans)
                        {
                            p.Parameters.Add(byteSpan + " " + name);
                            p.ParameterTypes.Add(byteSpan);
                            p.Pre.Add("fixed (byte* __" + parameter.Name + " = " + name + ")");
                            p.Arguments.Add("__" + parameter.Name);
                            p.Arguments.Add("(" + next.Type.ToDisplayString() + ")" + name + ".Length");
                        }

                        ++i;
                        continue;
                    }

                    if (pointee.SpecialType == SpecialType.System_Void)
                    {
                        foreach (Plan p in plans)
                        {
                            p.Parameters.Add("IntPtr " + name);
                            p.ParameterTypes.Add("IntPtr");
                            p.Arguments.Add("(void*)" + name);
                        }

                        continue;
                    }

                    if (pointee is INamedTypeSymbol pn && pn.Name == "b3LocalManifold" && next != null && NativeModel.IsInteger(next.Type))
                    {
                        foreach (Plan p in plans)
                        {
                            p.IsCollide = true;
                            p.Parameters.Add("Span<LocalManifoldPoint> points");
                            p.ParameterTypes.Add("Span<LocalManifoldPoint>");
                            p.Pre.Add("global::Box3D.Interop.b3LocalManifold __manifold = default;");
                            p.Pre.Add("fixed (LocalManifoldPoint* __points = points)");
                            p.Pre.Add("__manifold.points = (global::Box3D.Interop.b3LocalManifoldPoint*)__points;");
                            p.Arguments.Add("&__manifold");
                            p.Arguments.Add("points.Length");
                        }

                        ++i;
                        continue;
                    }

                    if (parameter.Name == "triangleA" && pointee.Name == "Vector3")
                    {
                        foreach (Plan p in plans)
                        {
                            p.Parameters.Add("Vector3 v1");
                            p.Parameters.Add("Vector3 v2");
                            p.Parameters.Add("Vector3 v3");
                            p.ParameterTypes.Add("Vector3");
                            p.ParameterTypes.Add("Vector3");
                            p.ParameterTypes.Add("Vector3");
                            p.Pre.Add("Vector3* __triangleA = stackalloc Vector3[3] { v1, v2, v3 };");
                            p.Arguments.Add("__triangleA");
                        }

                        continue;
                    }

                    if (_model.IsHandlePointer(type))
                    {
                        bool isHull = pointee.Name == "b3HullData" && isConst && _model.Structs.TryGetValue("b3BoxHull", out _);
                        var extra = new List<Plan>();
                        foreach (Plan p in plans)
                        {
                            if (isHull)
                            {
                                Plan box = p.Clone();
                                box.Parameters.Add("in BoxHull " + name);
                                box.ParameterTypes.Add("in BoxHull");
                                box.Pre.Add("fixed (BoxHull* __" + parameter.Name + " = &Unsafe.AsRef(in " + name + "))");
                                box.Arguments.Add("(global::Box3D.Interop.b3HullData*)__" + parameter.Name);
                                extra.Add(box);
                            }

                            p.Parameters.Add(Managed(type) + " " + name);
                            p.ParameterTypes.Add(Managed(type));
                            p.Arguments.Add(name + ".Pointer");
                        }

                        plans.AddRange(extra);
                        continue;
                    }

                    string element = Managed(pointee);
                    bool spanCount = next != null && NativeModel.IsInteger(next.Type) && IsCountName(next.Name);
                    if (spanCount && element != null && IsBlittableManaged(pointee))
                    {
                        string spanType = (isConst ? "ReadOnlySpan<" : "Span<") + element + ">";
                        foreach (Plan p in plans)
                        {
                            p.Parameters.Add(spanType + " " + name);
                            p.ParameterTypes.Add(spanType);
                            p.Pre.Add("fixed (" + element + "* __" + parameter.Name + " = " + name + ")");
                            p.Arguments.Add("(" + Full(pointee) + "*)__" + parameter.Name);
                            p.Arguments.Add("(" + next.Type.ToDisplayString() + ")" + name + ".Length");
                        }

                        ++i;
                        continue;
                    }

                    if (element == null)
                    {
                        Skip(function.Name + ": unsupported pointer parameter " + parameter.Name + " (" + type.ToDisplayString() + ")");
                        return null;
                    }

                    TypeCategory pointeeCategory = _model.CategoryOf(pointee);
                    if (pointeeCategory == TypeCategory.Managed)
                    {

                        StructInfo info = _model.Structs[pointee.Name];
                        foreach (Plan p in plans)
                        {
                            p.Parameters.Add("in " + element + " " + name);
                            p.ParameterTypes.Add("in " + element);
                            p.Pre.Add(Full(pointee) + " __" + parameter.Name + " = " + DefaultRawExpression(info) + ";");
                            p.Pre.Add(name + ".ToNative(ref __" + parameter.Name + ");");
                            AddPins(p, info, name, "__" + parameter.Name);
                            p.Arguments.Add("&__" + parameter.Name);
                        }

                        continue;
                    }

                    string modifier = isConst ? "in " : (IsOutParameter(function) ? "out " : "ref ");
                    foreach (Plan p in plans)
                    {
                        p.Parameters.Add(modifier + element + " " + name);
                        p.ParameterTypes.Add(modifier + element);
                        if (modifier == "out ")
                        {
                            p.Pre.Add(name + " = default;");
                        }

                        string address = isConst ? "&Unsafe.AsRef(in " + name + ")" : "&" + name;
                        p.Pre.Add("fixed (" + element + "* __" + parameter.Name + " = " + address + ")");
                        p.Arguments.Add(pointeeCategory == TypeCategory.Primitive || pointeeCategory == TypeCategory.Numerics
                            ? "__" + parameter.Name
                            : "(" + Full(pointee) + "*)__" + parameter.Name);
                    }

                    continue;
                }

                string managedType = Managed(type);
                if (managedType == null)
                {
                    Skip(function.Name + ": unsupported parameter " + parameter.Name + " (" + type.ToDisplayString() + ")");
                    return null;
                }

                if (_model.CategoryOf(type) == TypeCategory.Managed)
                {
                    StructInfo info = _model.Structs[type.Name];
                    foreach (Plan p in plans)
                    {
                        p.Parameters.Add("in " + managedType + " " + name);
                        p.ParameterTypes.Add("in " + managedType);
                        p.Pre.Add(Full(type) + " __" + parameter.Name + " = " + DefaultRawExpression(info) + ";");
                        p.Pre.Add(name + ".ToNative(ref __" + parameter.Name + ");");
                        AddPins(p, info, name, "__" + parameter.Name);
                        p.Arguments.Add("__" + parameter.Name);
                    }

                    continue;
                }

                foreach (Plan p in plans)
                {
                    p.Parameters.Add(managedType + " " + name);
                    p.ParameterTypes.Add(managedType);
                    p.Arguments.Add(ToNative(type, name));
                }
            }

            return plans;
        }

        private string PinString(Plan plan, string expression)
        {
            int index = plan.Pins++;
            plan.Pre.Add("int __length" + index + " = InteropHelpers.Utf8Capacity(" + expression + ");");
            plan.Pre.Add("Span<byte> __buffer" + index + " = __length" + index + " == 0 ? default : __length" + index + " <= 256 ? stackalloc byte[256] : new byte[__length" + index + "];");
            plan.Pre.Add("InteropHelpers.WriteUtf8(" + expression + ", __buffer" + index + ");");
            plan.Pre.Add("fixed (byte* __string" + index + " = __buffer" + index + ")");
            return "(" + expression + " == null ? null : (sbyte*)__string" + index + ")";
        }

        private void AddPins(Plan plan, StructInfo info, string managedPath, string rawPath)
        {
            foreach (FieldInfo field in info.Fields)
            {
                string name = Escape(field.Name);
                switch (field.Kind)
                {
                    case FieldKind.String:
                        string pointer = PinString(plan, managedPath + "." + name);
                        plan.Pre.Add(rawPath + "." + name + " = " + pointer + ";");
                        break;
                    case FieldKind.Array:
                        string element = Managed(field.Element);
                        if (element == null || !IsBlittableManaged(field.Element))
                        {
                            break;
                        }

                        int index = plan.Pins++;
                        plan.Pre.Add("fixed (" + element + "* __array" + index + " = " + managedPath + "." + name + (IsSpanStruct(info) ? ")" : ".Span)"));
                        plan.Pre.Add(rawPath + "." + name + " = (" + Full(field.Element) + "*)__array" + index + ";");
                        break;
                    case FieldKind.Value:
                        if (field.Type is INamedTypeSymbol nested && _model.CategoryOf(nested) == TypeCategory.Managed)
                        {
                            AddPins(plan, _model.Structs[nested.Name], managedPath + "." + name, rawPath + "." + name);
                        }

                        break;
                }
            }
        }

        private static bool IsPersistentCallback(IMethodSymbol function)
        {
            int underscore = function.Name.IndexOf('_');
            string member = underscore >= 0 ? function.Name.Substring(underscore + 1) : function.Name.Substring(2);
            return member.StartsWith("Set", StringComparison.Ordinal);
        }

        private static bool IsCountName(string name)
        {
            return name == "count" || name == "capacity" || name == "size"
                || name.EndsWith("Count", StringComparison.Ordinal) || name.EndsWith("Capacity", StringComparison.Ordinal);
        }

        private static bool IsOutParameter(IMethodSymbol function)
        {
            string name = function.Name;
            int underscore = name.IndexOf('_');
            string member = underscore >= 0 ? name.Substring(underscore + 1) : name.Substring(2);
            return member.StartsWith("Get", StringComparison.Ordinal);
        }

        private bool MapReturn(Plan plan, IMethodSymbol function)
        {
            ITypeSymbol type = function.ReturnType;
            if (plan.IsCollide)
            {
                plan.ReturnType = "LocalManifold";
                plan.Post.Add("__manifold.points = null;");
                plan.ReturnExpression = FromNative(_model.Types["b3LocalManifold"], "__manifold");
                return true;
            }

            if (type.SpecialType == SpecialType.System_Void)
            {
                plan.ReturnType = "void";
                return true;
            }

            if (type is IPointerTypeSymbol pointer)
            {
                if (pointer.PointedAtType.SpecialType == SpecialType.System_SByte)
                {
                    plan.ReturnType = "string";
                    plan.ReturnExpression = "InteropHelpers.ToManagedString({0})";
                    return true;
                }

                if (pointer.PointedAtType.SpecialType == SpecialType.System_Void)
                {
                    plan.ReturnType = "IntPtr";
                    plan.ReturnExpression = "(IntPtr){0}";
                    return true;
                }

                if (_model.IsHandlePointer(type) && _model.CategoryOf(pointer.PointedAtType) == TypeCategory.Handle)
                {
                    plan.ReturnType = Managed(type);
                    plan.ReturnExpression = "new " + plan.ReturnType + "({0})";
                    return true;
                }

                Skip(function.Name + ": unsupported pointer return " + type.ToDisplayString());
                return false;
            }

            if (_model.Categories.TryGetValue(type.Name, out TypeCategory category) && category == TypeCategory.ValueHandle)
            {
                plan.ReturnType = NativeModel.StripPrefix(type.Name);
                plan.ReturnExpression = "new " + plan.ReturnType + "({0})";
                return true;
            }

            string managed = Managed(type);
            if (managed == null)
            {
                Skip(function.Name + ": unsupported return type " + type.ToDisplayString());
                return false;
            }

            if (function.Name.StartsWith("b3Create", StringComparison.Ordinal) && function.Name.EndsWith("Joint", StringComparison.Ordinal)
                && type.Name == "b3JointId" && _typedJoints.Contains(function.Name.Substring("b3Create".Length)))
            {
                managed = function.Name.Substring("b3Create".Length);
                plan.ReturnType = managed;
                plan.ReturnExpression = "Unsafe.BitCast<global::Box3D.Interop.b3JointId, " + managed + ">({0})";
            }
            else
            {
                plan.ReturnType = managed;
                plan.ReturnExpression = FromNative(type, "{0}");
            }

            return true;
        }

        private sealed class ImmutableParameters
        {
            private readonly IReadOnlyList<IParameterSymbol> _items;

            public ImmutableParameters(IReadOnlyList<IParameterSymbol> items)
            {
                _items = items;
            }

            public int Count => _items.Count;

            public IParameterSymbol this[int index] => _items[index];
        }
    }
}
