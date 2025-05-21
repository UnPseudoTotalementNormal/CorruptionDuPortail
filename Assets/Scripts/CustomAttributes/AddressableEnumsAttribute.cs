#region

using System;
using UnityEngine;

#endregion

namespace CustomAttributes
{
    [AttributeUsage(AttributeTargets.Enum)]
    public class AddressableEnumsAttribute : PropertyAttribute
    {
        public string assetPath { get; }

        public AddressableEnumsAttribute(string _assetPath)
        {
            assetPath = _assetPath;
        }
    }
}