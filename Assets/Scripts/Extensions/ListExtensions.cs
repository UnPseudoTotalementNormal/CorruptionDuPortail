#region

using System;
using System.Collections.Generic;

#endregion

namespace Extensions
{
    public static class ListExtensions
    {
        public static void ChangeIndex<T>(this List<T> list, int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || oldIndex >= list.Count || newIndex < 0 || newIndex >= list.Count)
            {
                throw new ArgumentOutOfRangeException("Index out of range");
            }

            T item = list[oldIndex];
            list.RemoveAt(oldIndex);
            list.Insert(newIndex, item);
        }
    }
}