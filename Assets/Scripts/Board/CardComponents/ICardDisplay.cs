#region

using Characters;
using Cysharp.Threading.Tasks;

#endregion

namespace Board.CardComponents
{
    /// <summary>
    /// Interface for card display.
    /// </summary>
    public interface ICardDisplay
    {
        void SetPseudo(string _pseudo);
        void SetRoleText(string _roleText);
        void SetFaction(FactionType _factionType);
        void SetUnknown();
        void SetChainedOverlay(bool _isChained, bool _instant = false);
        UniTask SetRolePortrait(Role _role);
    }
}

