using System.Collections.Generic;
using Characters;
using Characters.Assets;
using CorruptionDuPortail.Domain;
using UI.Cards;
using UI.RoleCard;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace UI.LobbyRoles
{
    /// <summary>
    /// Drives the UITK lobby role-attribution app (LobbyRoles.uxml): a parchment page (mockup
    /// tools/maquettes/attribution-roles v5 F3) with the tabs, six counters, the compositions (presets) in the left
    /// margin and a faction-grouped grid of role cards, each with its two settings below it, "Au plus" (max, the pool
    /// cap) and "Au moins" (forced, the guaranteed minimum), then the ready status and the "Je suis prêt" checkbox. Built from an
    /// <see cref="ILobbyRolesDataSource"/>. Follows the InfoTable / RoleCardController conventions:
    /// [RequireComponent(UIDocument)], guarded TryInitialize in OnEnable+Start, Q-by-name, dynamic children in C#, BEM
    /// classes, look in USS (faction inks by code from the FactionDatabase).
    ///
    /// Rendered into a RenderTexture by <see cref="LobbyRolesRtPresenter"/> and shown on a tablet RawImage. The root is
    /// pickingMode=Ignore (never block the tablet's world input at the root; children pick per-element). The autoplay
    /// lobby driver finds its targets by these class names (unit, ctl/--forced, step-btn/val, preset-classic, footer,
    /// start, reason): keep them when changing the layout.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class LobbyRolesUitkController : MonoBehaviour
    {
        private const string RootName = "lobby-roles";
        private const string TitleClass = "lobby-roles__title";
        private const string TextBtnClass = "lobby-roles__text-btn";
        private const string TabsClass = "lobby-roles__tabs";
        private const string TabClass = "lobby-roles__tab";
        private const string TabActiveClass = "lobby-roles__tab--active";
        private const string TallyClass = "lobby-roles__tally";
        private const string TallyCellClass = "lobby-roles__tally-cell";
        private const string TallyCellFirstClass = "lobby-roles__tally-cell--first";
        private const string TallyKeyClass = "lobby-roles__tally-key";
        private const string TallyIconClass = "lobby-roles__tally-icon";
        private const string TallyValClass = "lobby-roles__tally-val";
        private const string StateBadClass = "lobby-roles__tally-val--bad";
        private const string BodyClass = "lobby-roles__body";
        private const string PresetsClass = "lobby-roles__presets";
        private const string PresetsTitleClass = "lobby-roles__presets-title";
        private const string PresetRowClass = "lobby-roles__preset-row";
        private const string PresetRowActiveClass = "lobby-roles__preset-row--active";
        private const string PresetClassicClass = "lobby-roles__preset-classic";
        private const string PresetClassicActiveClass = "lobby-roles__preset-classic--active";
        private const string ScrollWrapClass = "lobby-roles__scroll-wrap";
        private const string ScrollClass = "lobby-roles__scroll";
        private const string SectionClass = "lobby-roles__section";
        private const string SectionHeadClass = "lobby-roles__section-head";
        private const string SectionIconClass = "lobby-roles__section-icon";
        private const string GridClass = "lobby-roles__grid";
        private const string UnitClass = "lobby-roles__unit";
        private const string ControlsClass = "lobby-roles__controls";
        private const string CtlClass = "lobby-roles__ctl";
        private const string CtlForcedClass = "lobby-roles__ctl--forced";
        private const string CtlLabelClass = "lobby-roles__ctl-label";
        private const string StepperClass = "lobby-roles__stepper";
        private const string StepBtnClass = "lobby-roles__step-btn";
        private const string StepValClass = "lobby-roles__step-val";
        private const string FooterClass = "lobby-roles__footer";
        private const string ReasonClass = "lobby-roles__reason";
        private const string ReasonOkClass = "lobby-roles__reason--ok";
        private const string StartBtnClass = "lobby-roles__start";
        private const string StartBoxClass = "lobby-roles__start-box";
        private const string StartTickClass = "lobby-roles__start-tick";
        private const string StartOnClass = "lobby-roles__start--on";
        private const string OptionsClass = "lobby-roles__options";
        private const string OptionsTitleClass = "lobby-roles__options-title";
        private const string OptionsBtnClass = "lobby-roles__options-btn";

        private const int TabAttribution = 0;
        private const int TabOptions = 2;

        private const float CardWidth = 158f; // matches .lobby-roles__unit width; the card derives its 5:7 height

        // Faction render order (unknown is never shown). Labels fall back to these when no FactionDatabase is wired.
        private static readonly FactionType[] FactionOrder = { FactionType.anomaly, FactionType.chosen, FactionType.marginal };

        [SerializeField] private UIDocument _document;

        [Tooltip("A MonoBehaviour implementing ILobbyRolesDataSource (GameLobbyRolesDataSource / DemoLobbyRolesDataSource).")]
        [SerializeField] private MonoBehaviour _dataSourceBehaviour;

        [Tooltip("Optional SHARED FactionDatabase asset — the single source of faction labels/colours/icons (never " +
                 "duplicate these into USS). If null, fallback labels/colours are used (harness).")]
        [SerializeField] private FactionDatabase _factionDatabase;

        [Tooltip("Resolves a role's portrait (CharacterPortraits) -> Sprite for the card face. Wire the shared " +
                 "PortraitTable asset (same one RoleCard/the character bar use). Null = deep fallback art.")]
        [SerializeField] private PortraitTable _portraitTable;

        [Tooltip("The screen-space RoleCard overlay opened when a card is tapped (reused as-is). Null = tap does nothing.")]
        [SerializeField] private RoleCardController _roleCardOverlay;

        private const float PoolOpacity = 1f;
        private const float DimOpacity = 0.3f;  // roles out of the pool read as clearly muted at a glance
        private const int CardFadeMs = 200;     // opacity transition when a role enters/leaves the pool

        private VisualElement _root;
        private ScrollView _scroll;
        private ILobbyRolesDataSource _data;
        private bool _initialized;
        private bool _everBuilt;
        private int _activeTab = TabAttribution;

        // Last opacity shown per role, so a rebuilt card can animate FROM it TO the new target (a full Rebuild
        // recreates every card, so a fresh element born at the target opacity would otherwise never transition).
        private readonly Dictionary<RoleID, float> _cardOpacity = new();

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

            // Preserve the scroll position across the full rebuild (a stepper click raises OnChanged → Rebuild;
            // recreating the ScrollView would otherwise snap the grid back to the top).
            float savedScrollY = _scroll != null ? _scroll.scrollOffset.y : 0f;
            _root.Clear();
            _scroll = null;

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

            _root.Add(BuildTabs());

            if (_activeTab == TabOptions)
            {
                _root.Add(BuildOptions());
                return;
            }

            _root.Add(BuildTally(players, totalForced, totalMax, perFactionMax));

            var body = new VisualElement { pickingMode = PickingMode.Ignore };
            body.AddToClassList(BodyClass);
            VisualElement presets = BuildPresets();
            if (presets != null) body.Add(presets);

            var scrollWrap = new VisualElement { pickingMode = PickingMode.Ignore };
            scrollWrap.AddToClassList(ScrollWrapClass);
            _scroll = BuildGrid(roles);
            scrollWrap.Add(_scroll);
            body.Add(scrollWrap);
            _root.Add(body);

            float y = savedScrollY;
            ScrollView scroll = _scroll;
            scroll.schedule.Execute(() => scroll.scrollOffset = new Vector2(scroll.scrollOffset.x, y)).ExecuteLater(0);

            _root.Add(BuildFooter(players, roles));
        }

        // ---- tabs ----
        private VisualElement BuildTabs()
        {
            var tabs = new VisualElement();
            tabs.AddToClassList(TabsClass);
            tabs.Add(Tab("Rôles", TabAttribution));
            tabs.Add(Tab("Autres options", TabOptions));
            return tabs;

            VisualElement Tab(string label, int index)
            {
                var tab = new Button(() => { _activeTab = index; Rebuild(); }) { text = label };
                tab.AddToClassList(TextBtnClass);
                tab.AddToClassList(TitleClass);
                tab.AddToClassList(TabClass);
                if (_activeTab == index) tab.AddToClassList(TabActiveClass);
                return tab;
            }
        }

        // ---- "Autres options": the host-only dev "Démarrage forcé" (skips the all-ready condition; the server still
        // re-validates the composition). Non-host tablets show the page empty.
        private VisualElement BuildOptions()
        {
            var options = new VisualElement();
            options.AddToClassList(OptionsClass);
            if (!_data.IsHost()) return options;

            var title = new Label("Options de développement");
            title.AddToClassList(TitleClass);
            title.AddToClassList(OptionsTitleClass);
            options.Add(title);

            var force = new Button(() => _data.RequestForceStart()) { text = "Démarrage forcé" };
            force.AddToClassList(TextBtnClass);
            force.AddToClassList(OptionsBtnClass);
            options.Add(force);
            return options;
        }

        // ---- counters ----
        private VisualElement BuildTally(int players, int totalForced, int totalMax, Dictionary<FactionType, int> perFactionMax)
        {
            var tally = new VisualElement { pickingMode = PickingMode.Ignore };
            tally.AddToClassList(TallyClass);

            tally.Add(Cell("Joueurs", players, false, null, true));
            tally.Add(Cell("Garantis", totalForced, totalForced > players, null, false));
            tally.Add(Cell("Rôles possibles", totalMax, totalMax < players, null, false));
            foreach (FactionType faction in FactionOrder)
            {
                perFactionMax.TryGetValue(faction, out int m);
                tally.Add(Cell(FactionLabel(faction, true), m, false, FactionIcon(faction), false));
            }
            return tally;

            VisualElement Cell(string key, int val, bool bad, Sprite icon, bool first)
            {
                var cell = new VisualElement { pickingMode = PickingMode.Ignore };
                cell.AddToClassList(TallyCellClass);
                if (first) cell.AddToClassList(TallyCellFirstClass);

                var keyRow = new VisualElement { pickingMode = PickingMode.Ignore };
                keyRow.AddToClassList(TallyKeyClass);
                if (icon != null) keyRow.Add(Icon(icon, TallyIconClass));
                keyRow.Add(new Label(key) { pickingMode = PickingMode.Ignore });
                cell.Add(keyRow);

                var v = new Label(val.ToString()) { pickingMode = PickingMode.Ignore };
                v.AddToClassList(TallyValClass);
                if (bad) v.AddToClassList(StateBadClass);
                cell.Add(v);
                return cell;
            }
        }

        // ---- compositions (presets) in the left margin ----
        private VisualElement BuildPresets()
        {
            IReadOnlyList<LobbyPresetView> presets = _data.GetPresets();
            if (presets == null || presets.Count == 0) return null;

            int active = _data.GetActivePresetIndex(); // -1 = pool hand-edited ("Personnalisée")

            var column = new VisualElement();
            column.AddToClassList(PresetsClass);

            var title = new Label("Compositions") { pickingMode = PickingMode.Ignore };
            title.AddToClassList(TitleClass);
            title.AddToClassList(PresetsTitleClass);
            column.Add(title);

            for (int i = 0; i < presets.Count; i++)
            {
                int idx = i;
                var row = new Button(() => _data.ApplyPreset(idx)) { text = presets[i].Name }; // apply → OnChanged → Rebuild
                row.AddToClassList(TextBtnClass);
                row.AddToClassList(PresetRowClass);
                if (presets[i].IsClassic) row.AddToClassList(PresetClassicClass);
                if (active == idx)
                {
                    row.AddToClassList(PresetRowActiveClass);
                    if (presets[i].IsClassic) row.AddToClassList(PresetClassicActiveClass);
                }
                column.Add(row);
            }

            if (active < 0)
            {
                var custom = new Label("Personnalisée") { pickingMode = PickingMode.Ignore };
                custom.AddToClassList(PresetRowClass);
                custom.AddToClassList(PresetRowActiveClass);
                column.Add(custom);
            }
            return column;
        }

        // ---- the role grid, one section per faction ----
        private ScrollView BuildGrid(IReadOnlyList<LobbyRoleView> roles)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList(ScrollClass);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            foreach (FactionType faction in FactionOrder)
            {
                var inFaction = new List<LobbyRoleView>();
                foreach (LobbyRoleView r in roles)
                    if (r.Faction == faction) inFaction.Add(r);
                if (inFaction.Count == 0) continue;
                scroll.Add(BuildSection(faction, inFaction));
            }
            return scroll;
        }

        private VisualElement BuildSection(FactionType faction, List<LobbyRoleView> roles)
        {
            var section = new VisualElement { pickingMode = PickingMode.Ignore };
            section.AddToClassList(SectionClass);

            Color color = FactionColor(faction);
            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.AddToClassList(SectionHeadClass);
            Sprite icon = FactionIcon(faction);
            if (icon != null) head.Add(Icon(icon, SectionIconClass));
            var name = new Label(FactionLabel(faction, true)) { pickingMode = PickingMode.Ignore };
            name.AddToClassList(TitleClass);
            name.style.color = RoleSheet.InkOf(color);
            head.Add(name);
            section.Add(head);

            var grid = new VisualElement { pickingMode = PickingMode.Ignore };
            grid.AddToClassList(GridClass);
            foreach (LobbyRoleView r in roles) grid.Add(BuildCard(r));
            section.Add(grid);
            return section;
        }

        // A "unit" = the shared card face (portrait, gold name), then its two settings BELOW it. A guaranteed role carries
        // no mark on its card: its red "Au moins" says it (mockup v6 G1). The card stays a reusable pure visual.
        private VisualElement BuildCard(LobbyRoleView role)
        {
            var unit = new VisualElement();
            unit.AddToClassList(UnitClass);

            Sprite portrait = _portraitTable != null
                ? _portraitTable.Get((CharacterPortraitsValues.CharacterPortraits)role.PortraitId)
                : null;
            RoleCardElement card = new RoleCardElement().SetName(role.Name).SetPortrait(portrait).SetWidth(CardWidth);

            // Animate the pool ↔ dimmed opacity. A full Rebuild recreates the card on every stepper click, so a USS
            // transition never fires (no resolved "from" value on a newborn element — see the investigation case
            // file). An explicit value animation is driven frame-by-frame by the panel scheduler and works anyway.
            // The prev/target memory + no-op-when-equal guard keeps scroll/tab rebuilds from re-animating.
            float target = role.Max > 0 ? PoolOpacity : DimOpacity;
            float prev = _cardOpacity.TryGetValue(role.Id, out float p) ? p : target;
            _cardOpacity[role.Id] = target;
            card.style.opacity = prev;
            if (!Mathf.Approximately(prev, target))
                card.experimental.animation.Start(prev, target, CardFadeMs, (e, v) => e.style.opacity = v).Ease(Easing.OutCubic);

            RoleID clickedId = role.Id;
            card.RegisterCallback<ClickEvent>(_ => OpenDetail(clickedId)); // tap the card face → role detail overlay
            unit.Add(card);

            var controls = new VisualElement();
            controls.AddToClassList(ControlsClass);
            controls.Add(Stepper("Au plus", role.Max, role.Max <= 0, role.Max >= 15, false,
                () => _data.RequestSetMax(role.Id, role.Max - 1),
                () => _data.RequestSetMax(role.Id, role.Max + 1)));
            controls.Add(Stepper("Au moins", role.Forced, role.Forced <= 0, role.Forced >= role.Max, true,
                () => _data.RequestSetForced(role.Id, role.Forced - 1),
                () => _data.RequestSetForced(role.Id, role.Forced + 1)));
            unit.Add(controls);
            return unit;
        }

        // Tap a card → open the shared screen-space RoleCard overlay with that role's full authored detail.
        private void OpenDetail(RoleID id)
        {
            if (_roleCardOverlay == null || _data == null) return;
            Role role = _data.GetRole(id);
            if (role != null) _roleCardOverlay.Open(role);
        }

        private VisualElement Stepper(string label, int value, bool minusDisabled, bool plusDisabled, bool forced, System.Action onMinus, System.Action onPlus)
        {
            var ctl = new VisualElement();
            ctl.AddToClassList(CtlClass);
            if (forced) ctl.AddToClassList(CtlForcedClass);

            var lbl = new Label(label) { pickingMode = PickingMode.Ignore };
            lbl.AddToClassList(CtlLabelClass);
            ctl.Add(lbl);

            var stepper = new VisualElement();
            stepper.AddToClassList(StepperClass);

            var minus = new Button(onMinus) { text = "‹" };
            minus.AddToClassList(TextBtnClass);
            minus.AddToClassList(StepBtnClass);
            minus.SetEnabled(!minusDisabled);

            var val = new Label(value.ToString()) { pickingMode = PickingMode.Ignore };
            val.AddToClassList(StepValClass);

            var plus = new Button(onPlus) { text = "›" };
            plus.AddToClassList(TextBtnClass);
            plus.AddToClassList(StepBtnClass);
            plus.SetEnabled(!plusDisabled);

            stepper.Add(minus); stepper.Add(val); stepper.Add(plus);
            ctl.Add(stepper);
            return ctl;
        }

        // ---- footer ----
        private VisualElement BuildFooter(int players, IReadOnlyList<LobbyRoleView> roles)
        {
            var footer = new VisualElement();
            footer.AddToClassList(FooterClass);

            // Mirror of the server gate: the SAME pure CompositionValidator LobbyState delegates to — coverage
            // (Σmax ≥ players), guaranteed-fit (subsumes Σforced ≤ players), and the faction floors (≥1 anomaly,
            // ≥1 élu). UX-only: RequestStart re-validates server-side, so this mirror is never trusted for authority.
            var snapshotRoles = new List<RoleComposition>(roles.Count);
            foreach (LobbyRoleView r in roles)
            {
                snapshotRoles.Add(new RoleComposition(r.Faction, r.Max, r.Forced));
            }
            CompositionValidation validation = CompositionValidator.Validate(
                new CompositionSnapshot(players, snapshotRoles), _data.GetFactionMinimums());

            // Composition-invalid → the reason it can't start, in the status's place; valid → ready progress.
            // There is no start button: the server auto-starts once every player is ready and the composition is
            // valid (both re-checked server-side). Each player readies only themselves via the toggle below.
            int readyCount = _data.GetReadyCount();
            string status = validation.IsValid
                ? $"{readyCount} {(readyCount > 1 ? "joueurs prêts" : "joueur prêt")} sur {players}"
                : validation.FirstReason;

            var reason = new Label(status) { pickingMode = PickingMode.Ignore };
            reason.AddToClassList(ReasonClass);
            if (validation.IsValid) reason.AddToClassList(ReasonOkClass);
            footer.Add(reason);

            // An ink checkbox (mockup v6 B2): "Je suis prêt", ticked in red once ready; tapping again unticks it.
            bool localReady = _data.GetLocalReady();
            var toggle = new Button(() => _data.RequestSetReady(!localReady));
            toggle.AddToClassList(TextBtnClass);
            toggle.AddToClassList(StartBtnClass);
            if (localReady) toggle.AddToClassList(StartOnClass);
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.AddToClassList(StartBoxClass);
            if (localReady)
            {
                var tick = new VisualElement { pickingMode = PickingMode.Ignore };
                tick.AddToClassList(StartTickClass);
                box.Add(tick);
            }
            toggle.Add(box);
            var text = new Label("Je suis prêt") { pickingMode = PickingMode.Ignore };
            text.AddToClassList(TitleClass);
            toggle.Add(text);
            footer.Add(toggle);
            return footer;
        }

        // ---- helpers ----
        private static VisualElement Icon(Sprite sprite, string cssClass)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList(cssClass);
            icon.style.backgroundImage = new StyleBackground(sprite);
            return icon;
        }

        private Sprite FactionIcon(FactionType faction)
            => _factionDatabase != null && _factionDatabase.TryGet(faction, out FactionData data) ? data.icon : null;

        // The tablet speaks of a faction in the plural ("Anomalies", "Élus", "Marginaux"); the database holds the
        // singular display name, which the reasons use.
        private string FactionLabel(FactionType faction, bool plural)
        {
            if (plural)
            {
                switch (faction)
                {
                    case FactionType.anomaly: return "Anomalies";
                    case FactionType.chosen: return "Élus";
                    case FactionType.marginal: return "Marginaux";
                }
            }
            if (_factionDatabase != null && _factionDatabase.TryGet(faction, out FactionData data) && !string.IsNullOrEmpty(data.displayName))
                return data.displayName;
            return faction.ToString();
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
