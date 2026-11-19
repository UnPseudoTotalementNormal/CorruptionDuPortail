using System.Collections.Generic;
using System.Linq;
using Board;
using Characters.WinningConditions;
using CorruptionDuPortail.Domain.Localization;
using DG.Tweening;
using Diagnostics.Logs;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.EndGame
{
    /// <summary>
    /// End of the game, T9 maquettes menus v1 E3: each player first reads their own result full screen (« Victoire »
    /// or « Défaite », the winning camp under it), the word rises to the top, then every card turns over at its place
    /// (the winners gold, the losers dimmed, played by <see cref="GameLogic.GameStates.GameEndingState"/>), then the
    /// plank: « Terminer la partie » for the host, « Quitter la partie » and « L'hôte va terminer la partie. » for the
    /// others (C1 + C2). Lives on GameEndigStateUI.prefab; its pieces are built here, the planks are in the prefab.
    /// </summary>
    public class EndGameAnnounce : MonoBehaviour
    {
        // Sizes are in the 1920 x 1080 reference of the states canvas. Timings of the mockup (ms in the page): word at 0.8 s, rises at 2.4 s, cards from 2.7 s, plank at 4 s.
        public const float WordAt = 0.8f;
        public const float RiseAt = 2.4f;
        public const float CardsAt = 2.7f;
        public const float CardStagger = 0.1f;
        public const float PlankAt = 4f;

        private const string VictoryText = "Victoire"; // loc
        private const string DefeatText = "Défaite"; // loc

        [SerializeField] private TMP_FontAsset _font;
        [Tooltip("Assets/Art/Sprites/T9/Halo_Survol.png: the gold rim of a winner's card.")]
        [SerializeField] private Sprite _haloSprite;
        [Tooltip("The planks and the host-wait line, shown at PlankAt.")]
        [SerializeField] private CanvasGroup _actions;

        private static readonly Color Gold = new(0.941f, 0.722f, 0.353f); // #F0B85A, the mockup's gold
        private static readonly Color Red = new(0.878f, 0.376f, 0.31f); // #E0604F
        private static readonly Color Cream = new(0.965f, 0.906f, 0.776f); // #F6E7C6
        private static readonly Color HaloGold = new(1f, 0.806f, 0.325f); // the 3D hover outline's colour

        private Image _black;
        private RectTransform _wordBox;
        private TMP_Text _word;
        private TMP_Text _camp;
        private Sequence _sequence;

        /// <summary>Plays the announcement from its start. <paramref name="_teams"/> = the winning camps.</summary>
        public void Play(IReadOnlyCollection<WinningTeam> _teams, bool _localWins)
        {
            Build();
            _sequence?.Kill();
            _word.text = Loc.Tr(_localWins ? VictoryText : DefeatText);
            _word.color = _localWins ? Gold : Red;
            _camp.text = CampLine(_teams);
            GameLog.Info("ui", "end.announce", "result", _localWins ? "victory" : "defeat",
                "camps", string.Join(",", _teams.Select(_t => _t.ToString())));

            _black.color = new Color(0f, 0f, 0f, 1f);
            _wordBox.anchoredPosition = new Vector2(0f, -396f);
            _wordBox.localScale = Vector3.one * 1.15f;
            var _wordGroup = _wordBox.GetComponent<CanvasGroup>();
            _wordGroup.alpha = 0f;
            if (_actions != null)
            {
                _actions.alpha = 0f;
                _actions.interactable = false;
                _actions.blocksRaycasts = false;
            }

            _sequence = DOTween.Sequence().SetLink(gameObject)
                .Insert(WordAt, _wordGroup.DOFade(1f, 0.6f).SetEase(Ease.OutCubic))
                .Insert(WordAt, _wordBox.DOScale(1f, 0.6f).SetEase(Ease.OutCubic))
                .Insert(RiseAt, _wordBox.DOAnchorPosY(-36f, 0.6f).SetEase(Ease.OutCubic))
                .Insert(RiseAt, _wordBox.DOScale(0.45f, 0.6f).SetEase(Ease.OutCubic))
                .Insert(RiseAt, _black.DOFade(0f, 0.6f));
            if (_actions != null)
            {
                _sequence.Insert(PlankAt, _actions.DOFade(1f, 0.4f))
                    .InsertCallback(PlankAt, () =>
                    {
                        _actions.interactable = true;
                        _actions.blocksRaycasts = true;
                        GameLog.Info("ui", "end.plank.shown");
                    });
            }
        }

        /// <summary>A card turned over at the end: a winner gets the gold rim, a loser is dimmed.</summary>
        public void MarkCard(Card _card, bool _winner)
        {
            RectTransform _face = FrontOf(_card);
            if (_face == null)
            {
                GameLog.Warn("ui", "end.mark.missing", "reason", "no FrontCanvas on the card");
                return;
            }
            Transform _old = _face.Find("EndMark");
            if (_old != null) Destroy(_old.gameObject);
            var _go = new GameObject("EndMark", typeof(RectTransform), typeof(Image));
            var _rect = (RectTransform)_go.transform;
            _rect.SetParent(_face, false);
            _rect.SetAsLastSibling();
            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.one;
            var _image = _go.GetComponent<Image>();
            _image.raycastTarget = false;
            if (_winner && _haloSprite != null)
            {
                // In the card's own units (its canvas is scaled in the world): the glow reaches 12 % of its width.
                float _reach = Mathf.Max(0.0001f, _face.rect.width * 0.12f);
                _rect.offsetMin = new Vector2(-_reach, -_reach);
                _rect.offsetMax = new Vector2(_reach, _reach);
                _image.sprite = _haloSprite;
                _image.type = Image.Type.Sliced;
                // The sprite's glow is 40 px wide outside its line: scale its borders so the glow spans _reach.
                _image.pixelsPerUnitMultiplier = 40f / _reach;
                _image.color = new Color(HaloGold.r, HaloGold.g, HaloGold.b, 0f);
            }
            else
            {
                _rect.offsetMin = Vector2.zero;
                _rect.offsetMax = Vector2.zero;
                _image.color = new Color(0.02f, 0.02f, 0.03f, 0f);
            }
            _image.DOFade(_winner ? 1f : 0.55f, 0.4f).SetLink(_go);
        }

        // The card's root has no size: its face is the FrontCanvas (630 x 880), shown once the card is turned over.
        private static RectTransform FrontOf(Card _card)
        {
            if (_card == null) return null;
            foreach (RectTransform _rect in _card.GetComponentsInChildren<RectTransform>(true))
            {
                if (_rect.name == "FrontCanvas") return _rect;
            }
            return null;
        }

        private static string CampLine(IReadOnlyCollection<WinningTeam> _teams)
        {
            List<string> _names = _teams.Select(CampName).ToList();
            if (_names.Count == 0) return string.Empty;
            if (_names.Count == 1)
            {
                return _teams.First() == WinningTeam.alone ? Loc.Tr("Un solitaire l’emporte") : Loc.Tr("{0} l’emportent", _names[0]);
            }
            return Loc.Tr("{0} et {1} l’emportent", string.Join(", ", _names.Take(_names.Count - 1)), _names[^1]);
        }

        private static string CampName(WinningTeam _team) => _team switch
        {
            WinningTeam.chosen => Loc.Tr("Les Élus"),
            WinningTeam.anomaly => Loc.Tr("Les Anomalies"),
            WinningTeam.marginal => Loc.Tr("Les Marginaux"),
            _ => Loc.Tr("Les solitaires"),
        };

        private void Build()
        {
            if (_black != null) return;
            var _root = (RectTransform)transform;

            _black = NewChild<Image>("EndBlack", _root);
            Stretch((RectTransform)_black.transform);
            _black.raycastTarget = false;
            _black.transform.SetAsFirstSibling();

            var _boxGo = new GameObject("EndWord", typeof(RectTransform), typeof(CanvasGroup));
            _wordBox = (RectTransform)_boxGo.transform;
            _wordBox.SetParent(_root, false);
            _wordBox.anchorMin = _wordBox.anchorMax = new Vector2(0.5f, 1f);
            _wordBox.pivot = new Vector2(0.5f, 1f);
            _wordBox.sizeDelta = new Vector2(1920f, 320f);
            _boxGo.GetComponent<CanvasGroup>().blocksRaycasts = false;

            _word = NewText("Word", _wordBox, 204f, new Vector2(0f, 0f), 220f);
            _word.fontStyle = FontStyles.SmallCaps;
            _camp = NewText("Camp", _wordBox, 48f, new Vector2(0f, -230f), 70f);
            _camp.fontStyle = FontStyles.Italic;
            _camp.color = Cream;
        }

        private TMP_Text NewText(string _name, RectTransform _parent, float _size, Vector2 _pos, float _height)
        {
            var _text = NewChild<TextMeshProUGUI>(_name, _parent);
            var _rect = (RectTransform)_text.transform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 1f);
            _rect.pivot = new Vector2(0.5f, 1f);
            _rect.anchoredPosition = _pos;
            _rect.sizeDelta = new Vector2(1920f, _height);
            if (_font != null) _text.font = _font;
            _text.fontSize = _size;
            _text.alignment = TextAlignmentOptions.Center;
            _text.enableWordWrapping = false;
            _text.raycastTarget = false;
            return _text;
        }

        private static T NewChild<T>(string _name, RectTransform _parent) where T : Component
        {
            var _go = new GameObject(_name, typeof(RectTransform), typeof(T));
            _go.transform.SetParent(_parent, false);
            return _go.GetComponent<T>();
        }

        private static void Stretch(RectTransform _rect)
        {
            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.one;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
