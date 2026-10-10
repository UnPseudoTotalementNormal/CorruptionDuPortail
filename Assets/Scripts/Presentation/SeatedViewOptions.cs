using System;
using UnityEngine;

namespace Presentation
{
    /// <summary>
    /// Local, per-player trial options for the seated day view (board task T16, 2026-10-08: the designer asked to try
    /// cards standing up in first person and a round table). Presentation only, never networked: each player picks
    /// them in the pause menu and they persist in PlayerPrefs. A ScriptableObject channel (mirror
    /// <see cref="Avatars.CameraModeChannel"/>): the cards, the table switch and the pause panel reference the same
    /// asset, no singleton.
    /// </summary>
    [CreateAssetMenu(menuName = "Corruption/Seated View Options", fileName = "SeatedViewOptions")]
    public class SeatedViewOptions : ScriptableObject
    {
        private const string CardsStandKey = "Trial.CardsStand";
        private const string RoundTableKey = "Trial.RoundTable";

        [Tooltip("How far a standing card turns from flat (0) to facing the camera (1). A full stand hides the far line " +
                 "behind the near one.")]
        [Range(0f, 1f)]
        [SerializeField] private float _standAmount = 0.6f;

        /// <summary>How far a standing card turns towards the camera (0 = flat, 1 = facing it, like a hovered card).</summary>
        public float StandAmount
        {
            get => _standAmount;
            set => _standAmount = Mathf.Clamp01(value);
        }

        /// <summary>In the seated first-person vote, every card stands facing the camera (not only the hovered one).</summary>
        public bool CardsStand { get; private set; }

        /// <summary>The round placeholder table replaces the rectangular one.</summary>
        public bool RoundTable { get; private set; }

        public event Action OnChanged;

        // Domain reload is disabled in this project: reload the saved values whenever the asset is (re)enabled so a
        // value set by the previous Play session (or an autoplay run that did not persist) cannot leak.
        private void OnEnable()
        {
            CardsStand = PlayerPrefs.GetInt(CardsStandKey, 0) == 1;
            RoundTable = PlayerPrefs.GetInt(RoundTableKey, 0) == 1;
        }

        /// <summary>Set the standing-cards option. <paramref name="_persist"/> false = this session only (autoplay:
        /// PlayerPrefs are shared by every process on the machine).</summary>
        public void SetCardsStand(bool _value, bool _persist = true)
        {
            if (_persist)
            {
                PlayerPrefs.SetInt(CardsStandKey, _value ? 1 : 0);
            }
            if (_value == CardsStand)
            {
                return;
            }
            CardsStand = _value;
            OnChanged?.Invoke();
        }

        /// <summary>Set the round-table option (see <see cref="SetCardsStand"/> for <paramref name="_persist"/>).</summary>
        public void SetRoundTable(bool _value, bool _persist = true)
        {
            if (_persist)
            {
                PlayerPrefs.SetInt(RoundTableKey, _value ? 1 : 0);
            }
            if (_value == RoundTable)
            {
                return;
            }
            RoundTable = _value;
            OnChanged?.Invoke();
        }
    }
}
