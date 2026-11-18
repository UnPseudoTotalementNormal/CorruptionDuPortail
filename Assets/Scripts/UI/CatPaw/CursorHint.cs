using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UI.CatPaw
{
    /// <summary>What the pointer may do with the thing under it: nothing, click it, or it refuses right now.</summary>
    public enum CursorHint
    {
        None,
        Clickable,
        Refused,
    }

    /// <summary>The four paws drawn by Wouh (task T9, 2026-11-15): at rest, claws out, pressed, refused.</summary>
    public enum PawState
    {
        Idle,
        Claws,
        Press,
        Refused,
    }

    /// <summary>
    /// Implemented by anything clickable whose click can be refused at times (a greyed button, a power that cannot be
    /// used, a card the open picker does not offer), so the cat paw shows the cross before the player clicks.
    /// </summary>
    public interface ICursorHint
    {
        CursorHint CursorHint { get; }
    }

    public static class CursorHints
    {
        /// <summary>
        /// The hint of a hit object: its nearest <see cref="ICursorHint"/> (a button inside a card wins over the card),
        /// else a uGUI <see cref="Selectable"/>'s interactable flag, else any click handler counts as clickable.
        /// </summary>
        public static CursorHint Of(GameObject _hit)
        {
            if (_hit == null)
            {
                return CursorHint.None;
            }
            ICursorHint _hint = _hit.GetComponentInParent<ICursorHint>();
            if (_hint != null)
            {
                return _hint.CursorHint;
            }
            Selectable _selectable = _hit.GetComponentInParent<Selectable>();
            if (_selectable != null)
            {
                return _selectable.IsInteractable() ? CursorHint.Clickable : CursorHint.Refused;
            }
            return ExecuteEvents.GetEventHandler<IPointerClickHandler>(_hit) != null ? CursorHint.Clickable : CursorHint.None;
        }

        /// <summary>Pure rule (maquettes curseur-patte v1, C3): claws on what clicks, pressed while clicking it, the
        /// cross on what refuses (even while pressing), the resting paw everywhere else.</summary>
        public static PawState Paw(CursorHint _hint, bool _pressing)
        {
            switch (_hint)
            {
                case CursorHint.Refused:
                    return PawState.Refused;
                case CursorHint.Clickable:
                    return _pressing ? PawState.Press : PawState.Claws;
                default:
                    return PawState.Idle;
            }
        }
    }
}
