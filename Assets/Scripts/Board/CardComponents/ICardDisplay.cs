#region

using Characters;
using Cysharp.Threading.Tasks;
using UnityEngine;

#endregion

namespace Board.CardComponents
{
    /// <summary>
    /// Interface for card display.
    /// </summary>
    public interface ICardDisplay
    {
        /// <summary>
        /// Initialize the display with UI components.
        /// Must be called before using any other methods.
        /// </summary>
        void Initialize(CardVisualComponents _visualComponents);
        
        void SetPseudo(string _pseudo);
        void SetRoleText(string _roleText);
        void SetFaction(FactionType _factionType);
        void SetUnknown();
        void SetUnknownWithPseudo(string _pseudo);
        void SetChainedOverlay(bool _isChained, bool _instant = false);
        UniTask SetRolePortrait(Role _role);
        void ShowFrontSideInfo(Transform _voteCanvasTransform, Transform _cardEffectsParent);
        void ShowBackSideInfo(Transform _voteCanvasTransform, Transform _cardEffectsParent);
    }
}

