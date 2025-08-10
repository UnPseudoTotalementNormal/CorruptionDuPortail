using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

public class TextMaxWrapper : MonoBehaviour
{
    [SerializeField] TMP_Text tmp;
    [SerializeField] private RectTransform _rectTransform;
    [Tooltip("Nombre max de lignes avant ellipsis")]
    public int maxLines = 3;
    public string ellipsis = "...";
    [Tooltip("Conserver les balises RichText (simple)")]
    public bool preserveRichText = true;

    string originalText;
    private Vector2 lastRectSize;

    void Reset()
    {
        tmp = GetComponent<TMP_Text>();
        _rectTransform = GetComponent<RectTransform>();
    }

    void Start()
    {
        if (tmp == null) tmp = GetComponent<TMP_Text>();
        originalText = tmp.text;
        lastRectSize = _rectTransform.rect.size;
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
    }

    private void Update()
    {
        Vector2 _currentRectSize = _rectTransform.rect.size;
        if (_currentRectSize == lastRectSize)
        {
            return;
        }
        
        lastRectSize = _currentRectSize;
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
        TruncateNow();
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
    }

    private void OnTextChanged(Object _obj)
    {
        if (_obj != tmp)
        {
            return;
        }
        
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
        TruncateNow();
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        lastRectSize = _rectTransform.rect.size;
    }
    
    [ContextMenu("Truncate Now")]
    public void TruncateNow()
    {
        originalText = originalText ?? tmp.text;
        tmp.text = originalText;

        Canvas.ForceUpdateCanvases();
        tmp.ForceMeshUpdate();

        var _info = tmp.textInfo;
        if (_info == null || _info.lineCount <= maxLines) return;

        var _charInfo = _info.characterInfo;
        var _lineInfo = _info.lineInfo;

        int _last = _lineInfo[maxLines - 1].lastVisibleCharacterIndex;
        if (_last < 0) _last = _lineInfo[maxLines - 1].lastCharacterIndex;
        _last = Mathf.Clamp(_last, 0, _charInfo.Length - 1);

        for (int i = _last; i >= 0; i--)
        {
            int _sIndex = _charInfo[i].index;
            if (_sIndex < 0) continue;
            string _head = originalText.Substring(0, _sIndex + 1);
            string _closers = preserveRichText ? CloseOpenTags(originalText, _sIndex) : "";
            string _cand = _head + ellipsis + _closers;

            tmp.text = _cand;
            Canvas.ForceUpdateCanvases();
            tmp.ForceMeshUpdate();

            if (tmp.textInfo.lineCount <= maxLines)
            {
                return;
            }
        }

        tmp.text = ellipsis + (preserveRichText ? CloseOpenTags(originalText, -1) : "");
        tmp.ForceMeshUpdate();
    }

    static string CloseOpenTags(string _source, int _cutIndex)
    {
        var _stack = new Stack<string>();
        int _length = _cutIndex >= 0 ? Mathf.Min(_cutIndex + 1, _source.Length) : 0;
        int i = 0;
        while (i < _length)
        {
            if (_source[i] == '<')
            {
                int _gt = _source.IndexOf('>', i + 1);
                if (_gt == -1) break;
                string _tag = _source.Substring(i + 1, _gt - i - 1).Trim();
                if (_tag.StartsWith("/"))
                {
                    string _name = _tag.Substring(1).Split(new char[] { ' ', '=' }, System.StringSplitOptions.RemoveEmptyEntries)[0];
                    if (_stack.Count > 0 && _stack.Peek() == _name) _stack.Pop();
                    i = _gt + 1;
                    continue;
                }
                else
                {
                    string _name = _tag.Split(new char[] { ' ', '=' }, System.StringSplitOptions.RemoveEmptyEntries)[0];
                    string _lower = _name.ToLowerInvariant();
                    if (_lower == "br" || _lower.StartsWith("sprite")) { i = _gt + 1; continue; }
                    _stack.Push(_name);
                    i = _gt + 1;
                    continue;
                }
            }
            i++;
        }
        if (_stack.Count == 0) return "";
        var _sb = new System.Text.StringBuilder();
        while (_stack.Count > 0) _sb.AppendFormat("</{0}>", _stack.Pop());
        return _sb.ToString();
    }
}
