#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Linq;
using Characters;
using Characters.Powers;
using GameLogic;

namespace Autoplay
{
    /// <summary>
    /// Copied powers (Ugës's Marque d'Hurluberluges, Luma's Mélange des cartes, l'Incomplet's Réincarnation): what each
    /// seat holds, journaled on EVERY process so <c>tools/autoplay/analyze_copies.py</c> can check the rules (no copy
    /// used the night of the theft, one Marque copy per night, a spent copy gone everywhere) and compare peers.
    /// <para><c>power.copies &lt;phase&gt; | seat:[name{flags u=N},…] …</c> once per settled phase; flags: <c>S</c> one-shot
    /// stolen copy, <c>P</c> permanent copy, <c>M</c> tied to a Marque then <c>L</c> locked / <c>U</c> usable.
    /// <c>power.copyuse &lt;seat&gt; &lt;power&gt; day=N flags</c> when a bot starts using a copy.</para>
    /// </summary>
    public sealed partial class AutoplayDriver
    {
        private static bool IsCopy(Power _power)
            => _power && (_power.isStolenCopy.Value || _power.isCopiedPower.Value || _power.marqueSourceId.Value != 0);

        private static string CopyFlags(Power _power)
        {
            string _flags = _power.isStolenCopy.Value ? "S" : "P";
            if (_power.marqueSourceId.Value != 0)
            {
                _flags += _power.IsLockedByMarque() ? "ML" : "MU";
            }
            return _flags;
        }

        private void RecordCopies(string _phase)
        {
            string _seats = string.Join(" ", characterManager.GetCharacters(false)
                .Where(_c => _c && !_c.isFake && _c.role != null)
                .OrderBy(_c => _c.ownerClientId.Value)
                .Select(_c => (seat: _c.ownerClientId.Value, copies: _c.role.powers.Where(IsCopy)
                    .OrderBy(_p => _p.grantOrder.Value).ToList()))
                .Where(_s => _s.copies.Count > 0)
                .Select(_s => $"{_s.seat}:[{string.Join(",", _s.copies.Select(_p => $"{_p.powerName}{{{CopyFlags(_p)} u={_p.powerUseLeft.Value}}}"))}]"));
            Journal.Record("power.copies", $"{_phase} | {_seats}");
        }

        private readonly System.Collections.Generic.HashSet<string> holdsJournaled = new();

        // Lever "hold-power <power text>:<day>,…": the power stays unused before that day (journal power.hold once).
        private bool IsHeld(ulong _seat, Power _power)
        {
            if (string.IsNullOrEmpty(options.holdPowers) || !_power)
            {
                return false;
            }
            GameManager _gameManager = CompositionRoot.For(networkManager).GameManager;
            int _day = _gameManager != null ? _gameManager.currentDay : 0;
            foreach (string _entry in options.holdPowers.Split(','))
            {
                int _colon = _entry.LastIndexOf(':');
                if (_colon <= 0 || !int.TryParse(_entry.Substring(_colon + 1).Trim(), out int _untilDay))
                {
                    continue;
                }
                string _fragment = _entry.Substring(0, _colon).Trim();
                if (_day < _untilDay && _power.powerName.ToString().IndexOf(_fragment, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (holdsJournaled.Add($"{_seat}|{_power.powerName}|{_day}"))
                    {
                        Journal.Record("power.hold", $"{_seat} {_power.powerName} day={_day} until={_untilDay}");
                    }
                    return true;
                }
            }
            return false;
        }

        private void RecordCopyUse(ulong _seat, Power _power)
        {
            if (!IsCopy(_power))
            {
                return;
            }
            GameManager _gameManager = CompositionRoot.For(networkManager).GameManager;
            Journal.Record("power.copyuse", $"{_seat} {_power.powerName} day={(_gameManager != null ? _gameManager.currentDay : -1)} {CopyFlags(_power)}");
        }
    }
}
#endif
