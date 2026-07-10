#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

#endregion

public static class ReflectionHelper
{
    public static IEnumerable<Type> GetSubclassesOf(Type parentType)
    {
        return Assembly.GetAssembly(parentType)
            .GetTypes()
            .Where(type => type.IsSubclassOf(parentType));
    }

    public static void SetPrivateField(object obj, string fieldName, object value)
    {
        var field = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        field?.SetValue(obj, value);
    }

    public static object InvokePrivateMethod(object obj, string methodName, params object[] args)
    {
        var method = obj.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        return method?.Invoke(obj, args);
    }

    public static object GetPrivateField(object obj, string fieldName)
    {
        var field = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        return field?.GetValue(obj);
    }
}
