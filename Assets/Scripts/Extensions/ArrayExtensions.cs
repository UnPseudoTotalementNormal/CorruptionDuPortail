#region

using System;
using System.Collections.Generic;
using System.Linq;

#endregion

namespace Extensions
{
    public static class ArrayExtensions
    {
        public static int IndexOf<T>(this T[] array, T value)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            for (int i = 0; i < array.Length; i++)
            {
                if (EqualityComparer<T>.Default.Equals(array[i], value))
                {
                    return i;
                }
            }
            return -1;
        }
        
        public static int CountUsed<T>(this T[] _array, T _defaultValue)
        {
            if (_array == null) throw new ArgumentNullException(nameof(_array));
            return _array.Count(_item => !EqualityComparer<T>.Default.Equals(_item, _defaultValue));
        }
    }
}