using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Box3D.Tests;

public class PublicApiTests
{
    [Fact]
    public void NoRawPointersOrInteropTypesInPublicApi()
    {
        var offenders = new List<string>();
        foreach (Type type in Reflect.Safe.GetExportedTypes())
        {
            foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                IEnumerable<Type> types = member switch
                {
                    FieldInfo f => new[] { f.FieldType },
                    PropertyInfo p => new[] { p.PropertyType },
                    MethodInfo m => m.GetParameters().Select(x => x.ParameterType).Append(m.ReturnType),
                    ConstructorInfo c => c.GetParameters().Select(x => x.ParameterType),
                    _ => Array.Empty<Type>(),
                };

                if (types.Any(IsForbidden))
                {
                    offenders.Add(type.Name + "." + member.Name);
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static bool IsForbidden(Type type)
    {
        while (type.HasElementType)
        {
            if (type.IsPointer)
            {
                return true;
            }

            type = type.GetElementType();
        }

        return type.IsPointer || type.IsFunctionPointer || type.Namespace == "Box3D.Interop";
    }
}
