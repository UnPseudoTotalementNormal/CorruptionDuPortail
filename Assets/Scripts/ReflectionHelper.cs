using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static class ReflectionHelper
{
    public static IEnumerable<Type> GetSubclassesOf(Type parentType)
    {
        return Assembly.GetAssembly(parentType)
            .GetTypes()
            .Where(type => type.IsSubclassOf(parentType));
    }
}