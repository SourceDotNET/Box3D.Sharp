using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Box3D.Sharp.Generator
{
    internal sealed partial class Emitter
    {
        private static readonly Dictionary<string, string[]> s_callbackParameterNames = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "b3OverlapResultFcn", new[] { "shapeId" } },
            { "b3CastResultFcn", new[] { "shapeId", "point", "normal", "fraction", "userMaterialId", "triangleIndex", "childIndex" } },
            { "b3MoverFilterFcn", new[] { "shapeId" } },
            { "b3PlaneResultFcn", new[] { "shapeId", "planes" } },
            { "b3TreeQueryCallbackFcn", new[] { "proxyId", "userData" } },
            { "b3TreeQueryClosestCallbackFcn", new[] { "distanceSqrMin", "proxyId", "userData" } },
            { "b3TreeRayCastCallbackFcn", new[] { "input", "proxyId", "userData" } },
            { "b3TreeBoxCastCallbackFcn", new[] { "input", "proxyId", "userData" } },
            { "b3MeshQueryFcn", new[] { "a", "b", "c", "triangleIndex" } },
            { "b3CompoundQueryFcn", new[] { "compound", "childIndex" } },
        };

        private static readonly HashSet<string> s_spanCallbacks = new HashSet<string>(StringComparer.Ordinal) { "b3PlaneResultFcn" };

        private readonly Dictionary<string, HandlerInfo> _handlers = new Dictionary<string, HandlerInfo>(StringComparer.Ordinal);

        private HandlerInfo Handler(string nativeName, IFunctionPointerTypeSymbol pointer, IMethodSymbol function)
        {
            string typedef = nativeName.Replace("*", string.Empty).Trim();
            if (_handlers.TryGetValue(typedef, out HandlerInfo existing))
            {
                return existing;
            }

            string baseName = NativeModel.StripPrefix(typedef);
            foreach (string suffix in new[] { "CallbackFcn", "Fcn", "Callback" })
            {
                if (baseName.EndsWith(suffix, StringComparison.Ordinal))
                {
                    baseName = baseName.Substring(0, baseName.Length - suffix.Length);
                    break;
                }
            }

            IMethodSymbol signature = pointer.Signature;
            var info = new HandlerInfo
            {
                Interface = "I" + baseName + "Handler",
                Method = "On" + baseName,
                Thunk = baseName + "Thunk",
                Dispatch = baseName + "Dispatch",
                Pointer = pointer,
            };

            string returnType = signature.ReturnType.SpecialType == SpecialType.System_Void ? "void" : Managed(signature.ReturnType);
            if (returnType == null)
            {
                Skip(function.Name + ": callback " + typedef + " has an unsupported return type");
                return null;
            }

            info.ManagedReturn = returnType;
            s_callbackParameterNames.TryGetValue(typedef, out string[] names);
            int nameIndex = 0;
            IReadOnlyList<IParameterSymbol> parameters = signature.Parameters;
            for (int i = 0; i < parameters.Count; ++i)
            {
                ITypeSymbol type = parameters[i].Type;
                info.ThunkParameters.Add(Full(type) + " p" + i);
                if (i == parameters.Count - 1)
                {
                    break;
                }

                string name = names != null && nameIndex < names.Length ? names[nameIndex] : "arg" + nameIndex;
                nameIndex++;
                if (type is IPointerTypeSymbol p && !_model.IsHandlePointer(type))
                {
                    string element = Managed(p.PointedAtType);
                    if (element == null || !IsBlittableManaged(p.PointedAtType))
                    {
                        Skip(function.Name + ": callback " + typedef + " has an unsupported pointer parameter");
                        return null;
                    }

                    if (s_spanCallbacks.Contains(typedef) && i + 1 < parameters.Count - 1 && NativeModel.IsInteger(parameters[i + 1].Type))
                    {
                        info.ManagedParameters.Add("ReadOnlySpan<" + element + "> " + name);
                        info.ManagedTypes.Add("ReadOnlySpan<" + element + ">");
                        info.Conversions.Add("new ReadOnlySpan<" + element + ">(p" + i + ", p" + (i + 1) + ")");
                        info.ThunkParameters.Add(Full(parameters[i + 1].Type) + " p" + (i + 1));
                        ++i;
                        continue;
                    }

                    info.ManagedParameters.Add("in " + element + " " + name);
                    info.ManagedTypes.Add("in " + element);
                    info.Conversions.Add("in Unsafe.AsRef<" + element + ">(p" + i + ")");
                    continue;
                }

                string managed = Managed(type);
                if (managed == null)
                {
                    Skip(function.Name + ": callback " + typedef + " has an unsupported parameter");
                    return null;
                }

                info.ManagedParameters.Add(managed + " " + name);
                info.ManagedTypes.Add(managed);
                info.Conversions.Add(FromNative(type, "p" + i));
            }

            if (names == null)
            {
                Skip("Callback " + typedef + " has no parameter name table; generated generic names");
            }

            _handlers[typedef] = info;
            return info;
        }

        private void EmitHandlers()
        {
            var sb = new StringBuilder();
            foreach (HandlerInfo handler in _handlers.Values.OrderBy(h => h.Interface, StringComparer.Ordinal))
            {
                sb.Append("public interface ").Append(handler.Interface).Append("\n{\n    ").Append(handler.ManagedReturn).Append(" ").Append(handler.Method).Append("(")
                    .Append(string.Join(", ", handler.ManagedParameters)).Append(");\n}\n\n");
            }

            sb.Append("internal static unsafe class Callbacks\n{\n");
            foreach (HandlerInfo handler in _handlers.Values.OrderBy(h => h.Interface, StringComparer.Ordinal))
            {
                string pointerType = "delegate*<void*, " + string.Join(", ", handler.ManagedTypes.Concat(new[] { handler.ManagedReturn })) + ">";
                string nativeReturn = handler.Pointer.Signature.ReturnType.SpecialType == SpecialType.System_Void ? "void" : Full(handler.Pointer.Signature.ReturnType);
                string context = "p" + (handler.Pointer.Signature.Parameters.Length - 1);
                sb.Append("    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]\n");
                sb.Append("    internal static ").Append(nativeReturn).Append(" ").Append(handler.Thunk).Append("(")
                    .Append(string.Join(", ", handler.ThunkParameters)).Append(")\n    {\n");
                sb.Append("        CallbackContext* context = (CallbackContext*)").Append(context).Append(";\n");
                sb.Append("        try\n        {\n            ");
                string invoke = "((" + pointerType + ")context->Dispatch)(context->State" + string.Concat(handler.Conversions.Select(c => ", " + c)) + ")";
                sb.Append(handler.ManagedReturn == "void" ? invoke + ";\n" : "return " + invoke + ";\n");
                sb.Append("        }\n        catch (Exception exception)\n        {\n            context->Fail(exception);\n");
                if (handler.ManagedReturn != "void")
                {
                    sb.Append("            return default;\n");
                }

                sb.Append("        }\n    }\n\n");
                sb.Append("    internal static ").Append(handler.ManagedReturn).Append(" ").Append(handler.Dispatch).Append("<T>(void* state");
                sb.Append(string.Concat(handler.ManagedParameters.Select(p => ", " + p))).Append(")\n        where T : struct, ").Append(handler.Interface).Append("\n    {\n        ");
                if (handler.ManagedReturn != "void")
                {
                    sb.Append("return ");
                }

                IEnumerable<string> argumentNames = handler.ManagedParameters.Select(p =>
                {
                    string name = p.Substring(p.LastIndexOf(' ') + 1);
                    return p.StartsWith("in ", StringComparison.Ordinal) ? "in " + name : name;
                });
                sb.Append("Unsafe.AsRef<T>(state).").Append(handler.Method).Append("(").Append(string.Join(", ", argumentNames)).Append(");\n    }\n\n");
            }

            TrimBlankLine(sb);
            sb.Append("}\n");
            AddFile("Callbacks", sb.ToString());
        }

        private sealed class PropertyGroup
        {
            public string Name;
            public string Type;
            public Plan Getter;
            public Plan Setter;
        }

        private void EmitTarget(StringBuilder sb, Target target, Target source)
        {
            bool typedCopy = !ReferenceEquals(target, source);
            List<Plan> plans = source.Members;
            List<Plan> statics = typedCopy ? new List<Plan>() : target.Statics;
            bool forwardDestroy = false;
            if (plans.Count == 0 && statics.Count == 0 && !forwardDestroy)
            {
                return;
            }

            sb.Append("public static unsafe partial class ").Append(target.ExtensionClass).Append("\n{\n");
            if (plans.Count > 0 || forwardDestroy)
            {
                string receiverType = target.ByReference ? "in " + target.ManagedType : target.ManagedType;
                sb.Append("    extension(").Append(receiverType).Append(" ").Append(target.ReceiverName).Append(")\n    {\n");
                var members = new StringBuilder();
                EmitMembers(members, plans, target, false, typedCopy);
                if (forwardDestroy)
                {
                    members.Append("        public void Destroy(bool wakeAttached) => ((JointId)").Append(target.ReceiverName).Append(").Destroy(wakeAttached);\n\n");
                }

                TrimBlankLine(members);
                sb.Append(members);
                sb.Append("    }\n");
            }

            if (statics.Count > 0)
            {
                if (plans.Count > 0 || forwardDestroy)
                {
                    sb.Append("\n");
                }

                sb.Append("    extension(").Append(target.ManagedType).Append(")\n    {\n");
                var members = new StringBuilder();
                EmitMembers(members, statics, target, true, false);
                TrimBlankLine(members);
                sb.Append(members);
                sb.Append("    }\n");
            }

            sb.Append("}\n\n");
        }

        private void EmitStaticClass(StringBuilder sb)
        {
            sb.Append("public static unsafe partial class B3\n{\n");
            var members = new StringBuilder();
            EmitMembers(members, _static.Members, null, true, false, "    ");
            TrimBlankLine(members);
            sb.Append(members);
            sb.Append("}\n");
        }

        private void EmitMembers(StringBuilder sb, List<Plan> plans, Target target, bool isStatic, bool typedCopy, string indent = "        ")
        {
            List<PropertyGroup> properties = GroupProperties(plans);
            var consumed = new HashSet<Plan>();
            foreach (PropertyGroup property in properties)
            {
                consumed.Add(property.Getter);
                if (property.Setter != null)
                {
                    consumed.Add(property.Setter);
                }
            }

            string modifiers = isStatic ? "public static " : "public ";
            foreach (PropertyGroup property in properties)
            {
                sb.Append(indent).Append(modifiers).Append(property.Type).Append(" ").Append(property.Name).Append("\n");
                sb.Append(indent).Append("{\n");
                sb.Append(indent).Append("    get\n").Append(indent).Append("    {\n");
                AppendBody(sb, property.Getter, target, typedCopy, indent + "        ", null);
                sb.Append(indent).Append("    }\n");
                if (property.Setter != null)
                {
                    string parameter = property.Setter.Parameters[0];
                    string parameterName = parameter.Substring(parameter.LastIndexOf(' ') + 1);
                    sb.Append(indent).Append("    set\n").Append(indent).Append("    {\n");
                    AppendBody(sb, property.Setter, target, typedCopy, indent + "        ", parameterName == "value" ? null : property.Type + " " + parameterName + " = value;");
                    sb.Append(indent).Append("    }\n");
                }

                sb.Append(indent).Append("}\n\n");
            }

            foreach (Plan plan in plans)
            {
                if (consumed.Contains(plan))
                {
                    continue;
                }

                sb.Append(indent).Append(modifiers).Append(plan.ReturnType).Append(" ").Append(plan.Name);
                if (plan.TypeParameters.Count > 0)
                {
                    sb.Append("<").Append(string.Join(", ", plan.TypeParameters)).Append(">");
                }

                sb.Append("(").Append(string.Join(", ", plan.Parameters)).Append(")");
                foreach (string constraint in plan.Constraints)
                {
                    sb.Append("\n").Append(indent).Append("    ").Append(constraint);
                }

                sb.Append("\n").Append(indent).Append("{\n");
                AppendBody(sb, plan, target, typedCopy, indent + "    ", null);
                sb.Append(indent).Append("}\n\n");
            }
        }

        private List<PropertyGroup> GroupProperties(List<Plan> plans)
        {
            var result = new List<PropertyGroup>();
            var names = new HashSet<string>(plans.Select(p => p.Name), StringComparer.Ordinal);
            foreach (Plan plan in plans)
            {
                if (plan.Parameters.Count != 0 || plan.TypeParameters.Count != 0 || plan.ReturnType == "void")
                {
                    continue;
                }

                string name = null;
                if (plan.Name.StartsWith("Get", StringComparison.Ordinal) && plan.Name.Length > 3 && char.IsUpper(plan.Name[3]))
                {
                    name = plan.Name.Substring(3);
                }
                else if (plan.ReturnType == "bool" && (StartsWithWord(plan.Name, "Is") || StartsWithWord(plan.Name, "Are") || StartsWithWord(plan.Name, "Has")))
                {
                    name = plan.Name;
                }

                if (name == null || result.Any(r => r.Name == name) || (names.Contains(name) && name != plan.Name))
                {
                    continue;
                }

                var group = new PropertyGroup { Name = name, Type = plan.ReturnType, Getter = plan };
                foreach (string setterName in SetterNames(plan.Name))
                {
                    Plan setter = plans.FirstOrDefault(p => p.Name == setterName && p.Parameters.Count == 1 && p.TypeParameters.Count == 0
                        && p.ReturnType == "void" && SetterType(p) == plan.ReturnType);
                    if (setter != null)
                    {
                        group.Setter = setter;
                        break;
                    }
                }

                result.Add(group);
            }

            return result;
        }

        private static bool StartsWithWord(string name, string word)
        {
            return name.StartsWith(word, StringComparison.Ordinal) && name.Length > word.Length && char.IsUpper(name[word.Length]);
        }

        private static string SetterType(Plan plan)
        {
            string type = plan.ParameterTypes[0];
            if (type.StartsWith("ref ", StringComparison.Ordinal) || type.StartsWith("out ", StringComparison.Ordinal))
            {
                return null;
            }

            return type.StartsWith("in ", StringComparison.Ordinal) ? type.Substring(3) : type;
        }

        private static IEnumerable<string> SetterNames(string getter)
        {
            if (getter.StartsWith("Get", StringComparison.Ordinal))
            {
                yield return "Set" + getter.Substring(3);
                yield break;
            }

            string rest = getter.StartsWith("Are", StringComparison.Ordinal) ? getter.Substring(3) : getter.Substring(2);
            if (rest.EndsWith("Enabled", StringComparison.Ordinal))
            {
                yield return "Enable" + rest.Substring(0, rest.Length - "Enabled".Length);
            }
            else if (rest.EndsWith("Allowed", StringComparison.Ordinal))
            {
                yield return "Allow" + rest.Substring(0, rest.Length - "Allowed".Length);
            }

            yield return "Set" + rest;
        }

        private void AppendBody(StringBuilder sb, Plan plan, Target target, bool typedCopy, string indent, string firstLine)
        {
            var lines = new List<string>();
            if (firstLine != null)
            {
                lines.Add(firstLine);
            }

            List<string> arguments = new List<string>(plan.Arguments);
            List<string> preLines = new List<string>(plan.Pre);
            if (typedCopy && plan.Target != null)
            {
                string original = arguments[0];
                arguments[0] = "Unsafe.BitCast<" + target.ManagedType + ", global::Box3D.Interop.b3JointId>(" + target.ReceiverName + ")";
                for (int i = 0; i < preLines.Count; ++i)
                {
                    preLines[i] = preLines[i].Replace(original, arguments[0]);
                }
            }

            int depth = 0;
            int fixedDepth = 0;
            foreach (string pre in preLines)
            {
                lines.Add(new string(' ', depth * 4) + pre);
                if (pre.StartsWith("fixed ", StringComparison.Ordinal))
                {
                    lines.Add(new string(' ', depth * 4) + "{");
                    depth++;
                    fixedDepth++;
                }
            }

            string pad = new string(' ', depth * 4);
            string call = "global::Box3D.Interop.Native." + plan.Function.Name + "(" + string.Join(", ", arguments) + ")";
            bool returnsValue = plan.ReturnType != "void";
            bool nativeReturns = plan.Function.ReturnType.SpecialType != SpecialType.System_Void;
            if (nativeReturns)
            {
                lines.Add(pad + "var __native = " + call + ";");
            }
            else
            {
                lines.Add(pad + call + ";");
            }

            foreach (string post in plan.Post)
            {
                lines.Add(pad + post);
            }

            if (returnsValue)
            {
                string expression = plan.ReturnExpression == null ? "__native" : plan.ReturnExpression.Replace("{0}", "__native");
                lines.Add(pad + "return " + expression + ";");
            }

            for (int i = 0; i < fixedDepth; ++i)
            {
                depth--;
                lines.Add(new string(' ', depth * 4) + "}");
            }

            foreach (string line in lines)
            {
                sb.Append(indent).Append(line).Append("\n");
            }
        }
    }
}
