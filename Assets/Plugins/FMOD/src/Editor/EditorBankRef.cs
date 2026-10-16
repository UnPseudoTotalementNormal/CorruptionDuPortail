using System;
using System.Collections.Generic;
using UnityEngine;
namespace FMODUnity
{
    public class EditorBankRef : ScriptableObject
    {
        [SerializeField]
        public string Path;

        [SerializeField]
        public string Name;

        [SerializeField]
        public string StudioPath;

        [SerializeField]
        private Int64 lastModified;

        [SerializeField]
        public List<NameValuePair> FileSizes;

        public bool Exists;

        public DateTime LastModified
        {
            get { return new DateTime(lastModified); }
            set { lastModified = value.Ticks; }
        }

        public static string CalculateName(string filePath, string basePath)
        {
            string relativePath = filePath.Substring(basePath.Length + 1);
            string extension = System.IO.Path.GetExtension(relativePath);

            string name = relativePath.Substring(0, relativePath.Length - extension.Length);
            name = RuntimeUtils.GetCommonPlatformPath(name);

            return name;
        }

        public void SetPath(string filePath, string basePath)
        {
            Path = RuntimeUtils.GetCommonPlatformPath(filePath);
            Name = CalculateName(filePath, basePath);
            base.name = "bank:/" + Name + System.IO.Path.GetExtension(filePath);
        }

        public void SetStudioPath(string studioPath)
        {
            // [CdP patch] A bank whose strings are not in Master.strings.bank has no studio path (FMOD hands back
            // null): VoteCandle is built alone, Master is never rebuilt from the repo's project (it lacks events the
            // committed Master.bank holds). The CI's bank refresh threw here and failed the build.
            if (string.IsNullOrEmpty(studioPath))
            {
                StudioPath = "bank:/" + Name;
                return;
            }
            string stringCmp;
            stringCmp = System.IO.Path.GetFileName(Name);
            if (!studioPath.Contains(stringCmp))
            {
                // No match means localization
                studioPath = studioPath.Substring(0, studioPath.LastIndexOf("/") + 1);
                studioPath += stringCmp;
            }
            StudioPath = studioPath;
        }

        [Serializable]
        public class NameValuePair
        {
            public string Name;
            public long Value;

            public NameValuePair(string name, long value)
            {
                Name = name;
                Value = value;
            }
        }
    }
}
