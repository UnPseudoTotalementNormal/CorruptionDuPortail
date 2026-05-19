using System;
using TMPro;
using UnityEngine;

namespace UI.InfoTable
{
    public class InfoHeaderText : MonoBehaviour
    {
        [SerializeField] private TMP_Text headerText;

        private void Reset()
        {
            headerText = GetComponentInChildren<TMP_Text>();
        }

        public void SetText(string _text)
        {
            headerText.text = _text;
        }
    }
}
