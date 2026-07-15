using System.Collections.Generic;
using Characters;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI.LobbyRoles
{
    /// <summary>
    /// Drives the UITK lobby role-attribution app (LobbyRoles.uxml). Builds a faction-grouped grid of role cards
    /// from an <see cref="ILobbyRolesDataSource"/>, each card carrying two steppers — Max (pool cap) and Forcé
    /// (guaranteed minimum) — plus a live composition tally and a gated "Démarrer" read. Follows the
    /// <c>InfoTableUitkController</c> / <c>RoleCardController</c> conventions: [RequireComponent(UIDocument)],
    /// guarded TryInitialize in OnEnable+Start, Q-by-name, dynamic children in C#, BEM classes, tokens in USS.
    ///
    /// Rendered into a RenderTexture by <see cref="LobbyRolesRtPresenter"/> and shown on a tablet RawImage.
    /// Design polish (portraits, faction art, final palette) is provisional — see the design proto. The root is
    /// pickingMode=Ignore (never block the tablet's world input at the root; children pick per-element).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class LobbyRolesUitkController : MonoBehaviour
    {
        private const string RootName = "lobby-roles";
        private const string TallyClass = "lobby-roles__tally";
        private const string TallyCellClass = "lobby-roles__tally-cell";
        private const string TallyKeyClass = "lobby-roles__tally-key";
        private const string TallyValClass = "lobby-roles__tally-val";
        private const string SectionClass = "lobby-roles__section";
        private const string SectionHeadClass = "lobby-roles__section-head";
        private const string GridClass = "lobby-roles__grid";
        private const string CardClass = "lobby-roles__card";
        private const string CardActiveClass = "lobby-roles__card--active";
        private const string CardArtClass = "lobby-roles__card-art";
        private const string CardNameClass = "lobby-roles__card-name";
        private const string ForcedTagClass = "lobby-roles__forced-tag";
        private const string ControlsClass = "lobby-roles__controls";
        private const string CtlClass = "lobby-roles__ctl";
        private const string CtlLabelClass = "lobby-roles__ctl-label";
        private const string CtlForcedClass = "lobby-roles__ctl--forced";
        private const string StepperClass = "lobby-roles__stepper";
        private const string StepBtnClass = "lobby-roles__step-btn";
        private const string StepValClass = "lobby-roles__step-val";
        private const string FooterClass = "lobby-roles__footer";
        private const string ReasonClass = "lobby-roles__reason";
        private const string ReasonOkClass = "lobby-roles__reason--ok";
        private const string StartBtnClass = "lobby-roles__start";
        private const string StateBadClass = "lobby-roles__tally-val--bad";

        // Faction render order (unknown is never shown). Labels fall back to these when no FactionDatabase is wired.
        private static readonly FactionType[] FactionOrder = { FactionType.anomaly, FactionType.chosen, FactionType.marginal };

        [SerializeField] private UIDocument _document;

        [Tooltip("A MonoBehaviour implementing ILobbyRolesDataSource (GameLobbyRolesDataSource / DemoLobbyRolesDataSource).")]
        [SerializeField] private MonoBehaviour _dataSourceBehaviour;

        [Tooltip("Optional SHARED FactionDatabase asset — the single source of faction labels/colours (never duplicate " +
                 "these into USS). If null, fallback labels/colours are used (harness).")]
        [SerializeField] private FactionDatabase _factionDatabase;

        private VisualElement _root;
        private ILobbyRolesDataSource _data;
        private bool _initialized;

        private void OnEnable() => TryInitialize();
        private void Start() => TryInitialize();

        private void OnDisable()
        {
            if (_data != null) _data.OnChanged -= Rebuild;
            _initialized = false;
        }

        private void TryInitialize()
        {
            if (_initialized) return;
            if (_document == null) _document = GetComponent<UIDocument>();

            VisualElement docRoot = _document != null ? _document.rootVisualElement : null;
            if (docRoot == null) return;

            _root = docRoot.Q<VisualElement>(RootName);
            if (_root == null) return;

            _data = _dataSourceBehaviour as ILobbyRolesDataSource;
            if (_data == null)
            {
                Debug.LogWarning("[LobbyRoles] No ILobbyRolesDataSource wired — grid stays empty.");
                return;
            }

            // Never block the tablet's world input at the root (project trap); interactive children pick themselves.
            _root.pickingMode = PickingMode.Ignore;

            _data.OnChanged -= Rebuild;
            _data.OnChanged += Rebuild;
            _initialized = true;
            Rebuild();
        }

        private void Rebuild()
        {
            if (_root == null || _data == null) return;
            _root.Clear();

            int players = _data.GetPlayerCount();
            IReadOnlyList<LobbyRoleView> roles = _data.GetRoles();

            int totalMax = 0, totalForced = 0;
            var perFactionMax = new Dictionary<FactionType, int>();
            foreach (LobbyRoleView r in roles)
            {
                totalMax += r.Max;
                totalForced += r.Forced;
                perFactionMax.TryGetValue(r.Faction, out int cur);
                perFactionMax[r.Faction] = cur + r.Max;
            }

            _root.Add(BuildTally(players, totalForced, totalMax, perFactionMax));

            foreach (FactionType faction in FactionOrder)
            {
                var inFaction = new List<LobbyRoleView>();
                foreach (LobbyRoleView r in roles)
                {
                    if (r.Faction == faction) inFaction.Add(r);
                }
                if (inFaction.Count == 0) continue;
                _root.Add(BuildSection(faction, inFaction));
            }

            _root.Add(BuildFooter(players, totalForced, totalMax));
        }

        private VisualElement BuildTally(int players, int totalForced, int totalMax, Dictionary<FactionType, int> perFactionMax)
        {
            var tally = new VisualElement();
            tally.AddToClassList(TallyClass);
            tally.pickingMode = PickingMode.Ignore;

            tally.Add(Cell("Joueurs", players.ToString(), false));
            tally.Add(Cell("Forcés (min)", totalForced.ToString(), totalForced > players));
            tally.Add(Cell("Pool max", totalMax.ToString(), totalMax < players));
            foreach (FactionType faction in FactionOrder)
            {
                if (!perFactionMax.TryGetValue(faction, out int m)) continue;
                tally.Add(Cell(FactionLabel(faction), m.ToString(), false));
            }
            return tally;

            VisualElement Cell(string key, string val, bool bad)
            {
                var cell = new VisualElement();
                cell.AddToClassList(TallyCellClass);
                cell.pickingMode = PickingMode.Ignore;
                var k = new Label(key); k.AddToClassList(TallyKeyClass);
                var v = new Label(val); v.AddToClassList(TallyValClass);
                if (bad) v.AddToClassList(StateBadClass);
                cell.Add(k); cell.Add(v);
                return cell;
            }
        }

        private VisualElement BuildSection(FactionType faction, List<LobbyRoleView> roles)
        {
            var section = new VisualElement();
            section.AddToClassList(SectionClass);
            section.pickingMode = PickingMode.Ignore;

            var head = new Label(FactionLabel(faction).ToUpperInvariant());
            head.AddToClassList(SectionHeadClass);
            Color c = FactionColor(faction);
            head.style.color = c;
            section.Add(head);

            var grid = new VisualElement();
            grid.AddToClassList(GridClass);
            grid.pickingMode = PickingMode.Ignore;
            foreach (LobbyRoleView r in roles) grid.Add(BuildCard(r, c));
            section.Add(grid);
            return section;
        }

        private VisualElement BuildCard(LobbyRoleView role, Color factionColor)
        {
            var card = new VisualElement();
            card.AddToClassList(CardClass);
            if (role.Max > 0) card.AddToClassList(CardActiveClass);
            card.style.borderTopColor = card.style.borderBottomColor =
                card.style.borderLeftColor = card.style.borderRightColor = factionColor;

            var art = new VisualElement();
            art.AddToClassList(CardArtClass);
            art.style.backgroundColor = new Color(factionColor.r, factionColor.g, factionColor.b, role.Max > 0 ? 0.35f : 0.12f);
            card.Add(art);

            if (role.Forced > 0)
            {
                var tag = new Label("★ forcé " + role.Forced);
                tag.AddToClassList(ForcedTagClass);
                card.Add(tag);
            }

            var name = new Label(role.Name);
            name.AddToClassList(CardNameClass);
            card.Add(name);

            var controls = new VisualElement();
            controls.AddToClassList(ControlsClass);
            controls.Add(Stepper("Max", role.Max, role.Max <= 0, role.Max >= 15, false,
                () => _data.RequestSetMax(role.Id, role.Max - 1),
                () => _data.RequestSetMax(role.Id, role.Max + 1)));
            controls.Add(Stepper("Forcé", role.Forced, role.Forced <= 0, role.Forced >= role.Max, true,
                () => _data.RequestSetForced(role.Id, role.Forced - 1),
                () => _data.RequestSetForced(role.Id, role.Forced + 1)));
            card.Add(controls);
            return card;
        }

        private VisualElement Stepper(string label, int value, bool minusDisabled, bool plusDisabled, bool forced, System.Action onMinus, System.Action onPlus)
        {
            var ctl = new VisualElement();
            ctl.AddToClassList(CtlClass);
            if (forced) ctl.AddToClassList(CtlForcedClass);

            var lbl = new Label(label);
            lbl.AddToClassList(CtlLabelClass);
            ctl.Add(lbl);

            var stepper = new VisualElement();
            stepper.AddToClassList(StepperClass);

            var minus = new Button(onMinus) { text = "−" };
            minus.AddToClassList(StepBtnClass);
            minus.SetEnabled(!minusDisabled);

            var val = new Label(value.ToString());
            val.AddToClassList(StepValClass);

            var plus = new Button(onPlus) { text = "+" };
            plus.AddToClassList(StepBtnClass);
            plus.SetEnabled(!plusDisabled);

            stepper.Add(minus); stepper.Add(val); stepper.Add(plus);
            ctl.Add(stepper);
            return ctl;
        }

        private VisualElement BuildFooter(int players, int totalForced, int totalMax)
        {
            var footer = new VisualElement();
            footer.AddToClassList(FooterClass);

            // Gate mirrors LobbyState: Σforced <= players <= Σmax. The authoritative check re-runs server-side.
            bool ok = totalMax >= players && totalForced <= players;
            string msg;
            if (totalMax < players) msg = $"Pool trop petit : {players - totalMax} rôle(s) en moins que de joueurs.";
            else if (totalForced > players) msg = $"Trop de rôles forcés : {totalForced} garantis pour {players} joueurs.";
            else
            {
                int surplus = totalMax - players;
                msg = surplus > 0 ? $"Prêt · {surplus} carte(s) en surplus" : "Prêt";
            }

            var reason = new Label(msg);
            reason.AddToClassList(ReasonClass);
            if (ok) reason.AddToClassList(ReasonOkClass);
            footer.Add(reason);

            var start = new Button { text = "Démarrer la partie" };
            start.AddToClassList(StartBtnClass);
            start.SetEnabled(ok);
            footer.Add(start);
            return footer;
        }

        private string FactionLabel(FactionType faction)
        {
            if (_factionDatabase != null && _factionDatabase.TryGet(faction, out FactionData data) && !string.IsNullOrEmpty(data.displayName))
                return data.displayName;
            switch (faction)
            {
                case FactionType.anomaly: return "Anomalies";
                case FactionType.chosen: return "Élus";
                case FactionType.marginal: return "Marginaux";
                default: return faction.ToString();
            }
        }

        private Color FactionColor(FactionType faction)
        {
            if (_factionDatabase != null && _factionDatabase.TryGet(faction, out FactionData data))
                return data.color;
            switch (faction)
            {
                case FactionType.anomaly: return new Color(0.84f, 0.19f, 0.29f);
                case FactionType.chosen: return new Color(0.25f, 0.68f, 0.38f);
                case FactionType.marginal: return new Color(0.88f, 0.59f, 0.23f);
                default: return new Color(0.49f, 0.56f, 0.63f);
            }
        }
    }
}
