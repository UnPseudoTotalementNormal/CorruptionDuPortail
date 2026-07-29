namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Where a power lands in the role-presentation card (RoleCard). The card has exactly two visible
    /// sections — numbered active pills and bulleted passive rows — plus "not shown at all".
    /// </summary>
    public enum RoleCardSlot
    {
        /// <summary>Omitted from the card entirely.</summary>
        Hidden,

        /// <summary>Rendered as a numbered active-power pill.</summary>
        ActivePill,

        /// <summary>Rendered as a bulleted passive row.</summary>
        PassiveRow,
    }

    /// <summary>
    /// Pure, decision-only rule for what appears in the RoleCard and in which section — the single source of
    /// truth the controller (RoleCardController) routes both builders through, so the design intent is
    /// EditMode-testable without booting NGO or UI Toolkit. No engine types: the controller reads the power's
    /// fields / NetworkVariables and passes plain values.
    ///
    /// The rules, in order:
    /// 1. A power flagged <c>hideFromRoleCard</c> (an authored faction win-objective, not personal kit) is hidden.
    /// 2. A one-shot copied power (<c>isStolenCopy</c> — Ugues' Marque d'Hurluberluges, Luma's Mélange des
    ///    cartes) is hidden: showing it would leak that the role is real (not factice) and which power it copied.
    /// 3. A runtime-granted power flagged <c>hideFromRoleCardRuntime</c> (L'Incomplet's Réincarnation grants the
    ///    picked role's full active kit) is hidden for the same reason.
    /// 4. Otherwise the section is decided by <c>authoredIsPassive</c> — the DESIGN-time passive flag, NOT the
    ///    live one: Réincarnation flips its live <c>isPassive</c> to true post-use to disable itself, but it must
    ///    still read as its authored active power here.
    /// 5. A passive with no description text is hidden (nothing to render); an active pill shows regardless.
    /// </summary>
    public static class RoleCardPowerVisibility
    {
        public static RoleCardSlot Classify(
            bool authoredIsPassive,
            bool hideFromRoleCard,
            bool isStolenCopy,
            bool hideFromRoleCardRuntime,
            bool hasDescription)
        {
            if (hideFromRoleCard || isStolenCopy || hideFromRoleCardRuntime)
            {
                return RoleCardSlot.Hidden;
            }

            if (authoredIsPassive)
            {
                return hasDescription ? RoleCardSlot.PassiveRow : RoleCardSlot.Hidden;
            }

            return RoleCardSlot.ActivePill;
        }
    }
}
