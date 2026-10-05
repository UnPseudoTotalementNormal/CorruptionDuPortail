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
        if (method == null)
        {
            return null;
        }

        // Trailing optional parameters (e.g. an RPC's `RpcParams _params = default`) take their default value, so
        // callers keep passing only the meaningful arguments.
        var parameters = method.GetParameters();
        if (args != null && args.Length < parameters.Length)
        {
            var full = new object[parameters.Length];
            System.Array.Copy(args, full, args.Length);
            for (int i = args.Length; i < parameters.Length; i++)
            {
                full[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            }
            args = full;
        }
        return method.Invoke(obj, args);
    }

    public static object GetPrivateField(object obj, string fieldName)
    {
        var field = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        return field?.GetValue(obj);
    }
}
