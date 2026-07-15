using System.Collections.Generic;
using Characters;
using Characters.Assets;
using UI.Cards;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI.LobbyRoles
{
    /// <summary>
    /// Drives the UITK lobby role-attribution app (LobbyRoles.uxml). Builds a faction-grouped grid of role cards
    /// from an <see cref="ILobbyRolesDataSource"/>, each card carrying two steppers — Max (pool cap) and Forcé
    /// (guaranteed minimum) — plus a preset bar, tabs (Attribution / Ta partie = same grid filtered to max&gt;0 /
    /// Autres options), a live composition tally and a gated "Démarrer" read. Follows the InfoTable /
    /// RoleCardController conventions: [RequireComponent(UIDocument)], guarded TryInitialize in OnEnable+Start,
    /// Q-by-name, dynamic children in C#, BEM classes, tokens in USS.
    ///
    /// Rendered into a RenderTexture by <see cref="LobbyRolesRtPresenter"/> and shown on a tablet RawImage. Design
    /// polish (portraits, faction art, final palette) is provisional — see the design proto. The root is
    /// pickingMode=Ignore (never block the tablet's world input at the root; children pick per-element).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class LobbyRolesUitkController : MonoBehaviour
    {
        private const string RootName = "lobby-roles";
        private const string TopBarClass = "lobby-roles__topbar";
        private const string PresetClassicClass = "lobby-roles__preset-classic";
        private const string PresetOtherClass = "lobby-roles__preset-other";
        private const string TabsClass = "lobby-roles__tabs";
        private const string TabClass = "lobby-roles__tab";
        private const string TabActiveClass = "lobby-roles__tab--active";
        private const string TabBadgeClass = "lobby-roles__tab-badge";
        private const string TallyClass = "lobby-roles__tally";
        private const string TallyCellClass = "lobby-roles__tally-cell";
        private const string TallyKeyClass = "lobby-roles__tally-key";
        private const string TallyValClass = "lobby-roles__tally-val";
        private const string SectionClass = "lobby-roles__section";
        private const string SectionHeadClass = "lobby-roles__section-head";
        private const string GridClass = "lobby-roles__grid";
        private const string UnitClass = "lobby-roles__unit";
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
        private const string EmptyClass = "lobby-roles__deck-empty";
        private const string OptsClass = "lobby-roles__opts";
        private const string FooterClass = "lobby-roles__footer";
        private const string ReasonClass = "lobby-roles__reason";
        private const string ReasonOkClass = "lobby-roles__reason--ok";
        private const string StartBtnClass = "lobby-roles__start";
        private const string StateBadClass = "lobby-roles__tally-val--bad";

        private const int TabAttribution = 0;
        private const int TabDeck = 1;
        private const int TabOptions = 2;

        // Faction render order (unknown is never shown). Labels fall back to these when no FactionDatabase is wired.
        private static readonly FactionType[] FactionOrder = { FactionType.anomaly, FactionType.chosen, FactionType.marginal };

        [SerializeField] private UIDocument _document;

        [Tooltip("A MonoBehaviour implementing ILobbyRolesDataSource (GameLobbyRolesDataSource / DemoLobbyRolesDataSource).")]
        [SerializeField] private MonoBehaviour _dataSourceBehaviour;

        [Tooltip("Optional SHARED FactionDatabase asset — the single source of faction labels/colours (never duplicate " +
                 "these into USS). If null, fallback labels/colours are used (harness).")]
        [SerializeField] private FactionDatabase _factionDatabase;

        [Tooltip("Resolves a role's portrait (CharacterPortraits) -> Sprite for the card face. Wire the shared " +
                 "PortraitTable asset (same one RoleCard/the character bar use). Null = deep fallback art.")]
        [SerializeField] private PortraitTable _portraitTable;

        private VisualElement _root;
        private ILobbyRolesDataSource _data;
        private bool _initialized;
        private bool _everBuilt;
        private int _activeTab = TabAttribution;

        private void OnEnable() => TryInitialize();
        private void Start() => TryInitialize();

        // Retry until the first successful build: the panel tree may not be ready on the frame the data source
        // first raises OnChanged (the RtPresenter's PanelSettings swap rebuilds it). Once built, this no-ops;
        // later data-driven rebuilds re-resolve the root themselves.
        private void Update()
        {
            if (_initialized && !_everBuilt) Rebuild();
        }

        private void OnDisable()
        {
            if (_data != null) _data.OnChanged -= Rebuild;
            _initialized = false;
        }

        private void TryInitialize()
        {
            if (_initialized) return;
            if (_document == null) _document = GetComponent<UIDocument>();

            _data = _dataSourceBehaviour as ILobbyRolesDataSource;
            if (_data == null)
            {
                Debug.LogWarning("[LobbyRoles] No ILobbyRolesDataSource wired — grid stays empty.");
                return;
            }

            _data.OnChanged -= Rebuild;
            _data.OnChanged += Rebuild;
            _initialized = true;
            Rebuild();
        }

        private void Rebuild()
        {
            if (_data == null) return;

            // Re-resolve the root every rebuild: the RtPresenter swaps the UIDocument's PanelSettings to a runtime
            // clone, which rebuilds the visual tree — a root cached at init would be stale/detached (black panel).
            VisualElement docRoot = _document != null ? _document.rootVisualElement : null;
            _root = docRoot != null ? docRoot.Q<VisualElement>(RootName) : null;
            if (_root == null) return;

            // Never block the tablet's world input at the root (project trap); interactive children pick themselves.
            _root.pickingMode = PickingMode.Ignore;
            _everBuilt = true;
            _root.Clear();

            int players = _data.GetPlayerCount();
            IReadOnlyList<LobbyRoleView> roles = _data.GetRoles();

            int totalMax = 0, totalForced = 0, inPool = 0;
            var perFactionMax = new Dictionary<FactionType, int>();
            foreach (LobbyRoleView r in roles)
            {
                totalMax += r.Max;
                totalForced += r.Forced;
                if (r.Max > 0) inPool++;
                perFactionMax.TryGetValue(r.Faction, out int cur);
                perFactionMax[r.Faction] = cur + r.Max;
            }

            VisualElement presetBar = BuildPresetBar();
            if (presetBar != null) _root.Add(presetBar);
            _root.Add(BuildTabs(inPool));
            _root.Add(BuildTally(players, totalForced, totalMax, perFactionMax));
            _root.Add(BuildContent(roles));
            _root.Add(BuildFooter(players, totalForced, totalMax));
        }

        // ---- preset bar ----
        private VisualElement BuildPresetBar()
        {
            IReadOnlyList<LobbyPresetView> presets = _data.GetPresets();
            if (presets == null || presets.Count == 0) return null;

            var bar = new VisualElement();
            bar.AddToClassList(TopBarClass);

            int classicIndex = 0;
            for (int i = 0; i < presets.Count; i++) { if (presets[i].IsClassic) { classicIndex = i; break; } }

            int ci = classicIndex;
            var classic = new Button(() => _data.ApplyPreset(ci)) { text = "★ Preset classique" };
            classic.AddToClassList(PresetClassicClass);
            bar.Add(classic);

            for (int i = 0; i < presets.Count; i++)
            {
                if (i == classicIndex) continue;
                int idx = i;
                var other = new Button(() => _data.ApplyPreset(idx)) { text = presets[i].Name };
                other.AddToClassList(PresetOtherClass);
                other.tooltip = presets[i].Description;
                bar.Add(other);
            }
            return bar;
        }

        // ---- tabs ----
        private VisualElement BuildTabs(int inPool)
        {
            var tabs = new VisualElement();
            tabs.AddToClassList(TabsClass);
            tabs.Add(Tab("Attribution de rôle", TabAttribution, -1));
            tabs.Add(Tab("Ta partie", TabDeck, inPool));
            tabs.Add(Tab("Autres options…", TabOptions, -1));
            return tabs;

            VisualElement Tab(string label, int index, int badge)
            {
                var tab = new Button(() => { _activeTab = index; Rebuild(); }) { text = label };
                tab.AddToClassList(TabClass);
                if (_activeTab == index) tab.AddToClassList(TabActiveClass);
                if (badge >= 0)
                {
                    var b = new Label(badge.ToString());
                    b.AddToClassList(TabBadgeClass);
                    tab.Add(b);
                }
                return tab;
            }
        }

        // ---- content per tab ----
        private VisualElement BuildContent(IReadOnlyList<LobbyRoleView> roles)
        {
            var content = new VisualElement();
            content.pickingMode = PickingMode.Ignore;

            if (_activeTab == TabOptions)
            {
                var opts = new Label("« Autres options… » — vide pour l'instant.");
                opts.AddToClassList(OptsClass);
                content.Add(opts);
                return content;
            }

            bool deckOnly = _activeTab == TabDeck;
            int shown = 0;
            foreach (FactionType faction in FactionOrder)
            {
                var inFaction = new List<LobbyRoleView>();
                foreach (LobbyRoleView r in roles)
                {
                    if (r.Faction != faction) continue;
                    if (deckOnly && r.Max <= 0) continue; // "Ta partie" = same grid filtered to max > 0
                    inFaction.Add(r);
                }
                if (inFaction.Count == 0) continue;
                content.Add(BuildSection(faction, inFaction));
                shown += inFaction.Count;
            }

            if (deckOnly && shown == 0)
            {
                var empty = new Label("Aucun rôle dans le pool. Ajoute des « Max » ou charge un preset.");
                empty.AddToClassList(EmptyClass);
                content.Add(empty);
            }
            return content;
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

        // A "unit" = the pure 5:7 portrait card (art fills the face, name overlaid at the bottom, forced tag) with
        // the Max/Forcé steppers BELOW it — the card itself stays a reusable pure-visual, controls live outside.
        private VisualElement BuildCard(LobbyRoleView role, Color factionColor)
        {
            var unit = new VisualElement();
            unit.AddToClassList(UnitClass);

            // THE reusable card face (portrait fills, gold name, faction frame) — one component, uniform everywhere.
            Sprite portrait = _portraitTable != null
                ? _portraitTable.Get((CharacterPortraitsValues.CharacterPortraits)role.PortraitId)
                : null;
            RoleCardElement card = RoleCardElement.Create(role.Name, portrait, factionColor);
            card.style.opacity = role.Max > 0 ? 1f : 0.5f; // dim roles not in the pool

            if (role.Forced > 0)
            {
                var tag = new Label("★ forcé " + role.Forced);
                tag.AddToClassList(ForcedTagClass);
                card.Add(tag);
            }
            unit.Add(card);

            var controls = new VisualElement();
            controls.AddToClassList(ControlsClass);
            controls.Add(Stepper("Max", role.Max, role.Max <= 0, role.Max >= 15, false,
                () => _data.RequestSetMax(role.Id, role.Max - 1),
                () => _data.RequestSetMax(role.Id, role.Max + 1)));
            controls.Add(Stepper("Forcé", role.Forced, role.Forced <= 0, role.Forced >= role.Max, true,
                () => _data.RequestSetForced(role.Id, role.Forced - 1),
                () => _data.RequestSetForced(role.Id, role.Forced + 1)));
            unit.Add(controls);
            return unit;
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
