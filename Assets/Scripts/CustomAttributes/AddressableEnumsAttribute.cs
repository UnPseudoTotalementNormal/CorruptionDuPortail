using System;
using UnityEngine;

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