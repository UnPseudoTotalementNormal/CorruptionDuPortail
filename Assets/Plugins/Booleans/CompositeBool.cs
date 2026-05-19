using System.Collections.Generic;

namespace Booleans
{
    /// <summary>
    /// A class that allows you to set multiple reasons for a boolean value to be true or false. If any reason is false, the overall value is false.
    /// This can be useful for things like enabling/disabling features based on multiple conditions.
    /// </summary>
    public class CompositeBool<T>
    {
        private Dictionary<T, bool> booleans = new();

        public static implicit operator bool(CompositeBool<T> _compositeBool)
        {
            return _compositeBool?.IsTrue() ?? false;
        }
        
        /// <summary>
        /// Sets the value for a specific reason.
        /// </summary>
        public void Set(T reason, bool value)
        {
            booleans[reason] = value;
        }

        /// <summary>
        /// Gets the value for a specific reason.
        /// </summary>
        public bool Get(T reason)
        {
            return booleans.GetValueOrDefault(reason, true);
        }

        public List<T> GetReasonsWithValue(bool value)
        {
            List<T> reasons = new();
            foreach (KeyValuePair<T, bool> keyValuePair in booleans)
            {
                if (keyValuePair.Value == value)
                {
                    reasons.Add(keyValuePair.Key);
                }
            }
            return reasons;
        }

        private bool IsTrue()
        {
            foreach (KeyValuePair<T, bool> keyValuePair in booleans)
            {
                if (!keyValuePair.Value)
                {
                    return false;
                }
            }
            return true;
        }
    }
}