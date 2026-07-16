using System.Collections.Generic;
using Characters;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// A required minimum number of REAL players of a given faction (e.g. anomaly ≥ 1, chosen ≥ 1).
    /// Adding/removing one entry is the whole "add/remove a faction rule" surface. Pure (NFR4).
    /// </summary>
    public readonly struct FactionMinimum
    {
        public FactionType Faction { get; }
        public int Min { get; }

        public FactionMinimum(FactionType faction, int min)
        {
            Faction = faction;
            Min = min;
        }
    }

    /// <summary>One role's composition inputs, supplied in frozen pool order.</summary>
    public readonly struct RoleComposition
    {
        public FactionType Faction { get; }
        public int Max { get; }
        public int Forced { get; }

        public RoleComposition(FactionType faction, int max, int forced)
        {
            Faction = faction;
            Max = max;
            Forced = forced;
        }
    }

    /// <summary>The lobby composition to validate: connected player count + the per-role pool.</summary>
    public readonly struct CompositionSnapshot
    {
        public int Players { get; }
        public IReadOnlyList<RoleComposition> Roles { get; }

        public CompositionSnapshot(int players, IReadOnlyList<RoleComposition> roles)
        {
            Players = players;
            Roles = roles;
        }
    }

    /// <summary>A single broken rule — a human-readable reason plus the faction to highlight (null = a scalar rule).</summary>
    public readonly struct CompositionFailure
    {
        public string RuleId { get; }
        public string Reason { get; }
        public FactionType? OffendingFaction { get; }

        public CompositionFailure(string ruleId, string reason, FactionType? offendingFaction)
        {
            RuleId = ruleId;
            Reason = reason;
            OffendingFaction = offendingFaction;
        }
    }

    /// <summary>The result of evaluating a snapshot against the rule set.</summary>
    public readonly struct CompositionValidation
    {
        public bool IsValid { get; }
        public IReadOnlyList<CompositionFailure> Failures { get; }

        public CompositionValidation(bool isValid, IReadOnlyList<CompositionFailure> failures)
        {
            IsValid = isValid;
            Failures = failures;
        }

        /// <summary>The first failure's reason, or empty when valid. Convenience for a one-line UI/log.</summary>
        public string FirstReason =>
            Failures != null && Failures.Count > 0 ? Failures[0].Reason : string.Empty;
    }

    /// <summary>
    /// Pure, engine-free composition rule evaluator (NFR4). The SINGLE source of truth shared by the server
    /// start gate (LobbyState) and the client footer mirror, so the two can never disagree. Its feasibility
    /// maths mirror exactly what <see cref="RoleDistributor"/> can GUARANTEE: the distributor protects `min`
    /// copies of each required faction from the fake draw and the real loop drains every un-faked copy, so a
    /// protected copy is always assigned real. "Add/remove a rule" = edit the minimum list (faction floors are
    /// data); if rule KINDS ever proliferate, promote to an ICompositionRule list — not now (YAGNI).
    /// </summary>
    public static class CompositionValidator
    {
        public static CompositionValidation Validate(
            CompositionSnapshot snapshot,
            IReadOnlyList<FactionMinimum> minimums)
        {
            var failures = new List<CompositionFailure>();
            IReadOnlyList<RoleComposition> roles = snapshot.Roles ?? System.Array.Empty<RoleComposition>();
            int players = snapshot.Players;

            // Rule "coverage": enough roles to cover every player (Σmax ≥ players).
            int totalMax = 0;
            for (int i = 0; i < roles.Count; i++)
            {
                totalMax += roles[i].Max;
            }

            if (players > totalMax)
            {
                failures.Add(new CompositionFailure(
                    "coverage",
                    $"Pool trop petit : {totalMax} rôle(s) pour {players} joueur(s).",
                    null));
            }

            // Guaranteed reals = Σ clamped-forced (all roles) + per-faction top-up to reach each minimum.
            // This is exactly Σ protect from RoleDistributor's reservation pass — the gate↔distributor contract.
            // Track forced per faction so a guaranteed-fit failure can point at the faction to lower (Samus's rule).
            int guaranteed = 0;
            var forcedByFaction = new Dictionary<FactionType, int>();
            for (int i = 0; i < roles.Count; i++)
            {
                int clampedForced = ClampForced(roles[i]);
                guaranteed += clampedForced;
                forcedByFaction.TryGetValue(roles[i].Faction, out int prior);
                forcedByFaction[roles[i].Faction] = prior + clampedForced;
            }

            if (minimums != null)
            {
                for (int m = 0; m < minimums.Count; m++)
                {
                    FactionMinimum fm = minimums[m];
                    if (fm.Min <= 0)
                    {
                        continue;
                    }

                    int factionMax = 0;
                    int factionForced = 0;
                    for (int i = 0; i < roles.Count; i++)
                    {
                        if (roles[i].Faction == fm.Faction)
                        {
                            factionMax += roles[i].Max;
                            factionForced += ClampForced(roles[i]);
                        }
                    }

                    // Rule "faction-capacity": the faction must have enough pool capacity to place `min` reals.
                    if (factionMax < fm.Min)
                    {
                        failures.Add(new CompositionFailure(
                            "faction-capacity",
                            factionMax == 0
                                ? $"Aucun rôle {Label(fm.Faction)} dans la partie (minimum {fm.Min})."
                                : $"Pas assez de rôles {Label(fm.Faction)} : {factionMax} disponible(s) pour un minimum de {fm.Min}.",
                            fm.Faction));
                    }

                    // Only the deficit beyond what this faction's per-role forced already guarantees adds cost.
                    int topUp = fm.Min - factionForced;
                    if (topUp > 0)
                    {
                        guaranteed += topUp;
                    }
                }
            }

            // Rule "guaranteed-fit": the guaranteed reals must fit within the player count.
            // Subsumes the old Σforced ≤ players floor (guaranteed ≥ Σforced).
            if (guaranteed > players)
            {
                // Point at the faction carrying the most forced — the one to lower (AC1 / Samus's readable rule).
                FactionType? worst = null;
                int worstForced = 0;
                foreach (KeyValuePair<FactionType, int> kv in forcedByFaction)
                {
                    if (kv.Value > worstForced)
                    {
                        worstForced = kv.Value;
                        worst = kv.Key;
                    }
                }

                string hint = worst.HasValue
                    ? $"Baisse le forcé {Label(worst.Value)} ou ajoute un joueur."
                    : "Baisse un forcé ou ajoute un joueur.";
                failures.Add(new CompositionFailure(
                    "guaranteed-fit",
                    $"Trop de rôles garantis : {guaranteed} pour {players} joueur(s). {hint}",
                    worst));
            }

            return new CompositionValidation(failures.Count == 0, failures);
        }

        // forced is clamped to the role's own max — an authored max=1/forced=3 counts as 1 guaranteed real,
        // exactly like RoleDistributor (fakeable = max − forced clamped ≥ 0 ⇒ protect ≤ max).
        private static int ClampForced(RoleComposition role)
        {
            int forced = role.Forced;
            if (forced < 0)
            {
                return 0;
            }

            return forced > role.Max ? role.Max : forced;
        }

        // Domain-level fallback labels for logs/reasons. The UI mirror may localise via FactionDatabase.
        private static string Label(FactionType faction)
        {
            switch (faction)
            {
                case FactionType.anomaly: return "Anomalie";
                case FactionType.chosen: return "Élu";
                case FactionType.marginal: return "Marginal";
                default: return faction.ToString();
            }
        }
    }
}
