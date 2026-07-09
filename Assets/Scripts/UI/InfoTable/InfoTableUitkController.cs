using UnityEngine;
using UnityEngine.UIElements;

namespace UI.InfoTable
{
    /// <summary>
    /// Drives the UITK deduction board (InfoTable.uxml). Builds a Players×Roles grid from an
    /// <see cref="IInfoTableDataSource"/>, records suspicions through an <see cref="InfoTableModel"/>, and paints
    /// conflict/lock visuals. The UITK replacement for the retired uGUI <c>InfoTableSystem</c>; follows the
    /// <c>RoleCardController</c> conventions (guarded init, Q-by-name, dynamic children, BEM classes).
    ///
    /// Visual language ("Dossier" direction): each cell is a 3-segment control — Sûr (✓) / Je pense (?) /
    /// Pas lui (✗) — so all three states stay directly clickable (re-clicking the active one clears it to None).
    /// A footer "context bar" carries per-role capacity counters + a conflict banner. Depth is faked with USS
    /// bevels/insets (no gradients/shadows in UITK).
    ///
    /// Responsive: the board reads like the SAME dossier breathing at any size. The controller computes a tier
    /// from the player/role COUNT (UITK runtime has no @media, and the RenderTexture is a fixed size, so pixel
    /// media-queries would measure the wrong thing) and stamps ONE class on the root; the USS drives the density
    /// (row floor, glyph size, padding, header/footer compaction). Few players/roles = the lush "comfort" look
    /// untouched; past the density floor the body becomes a vertical ScrollView with a frozen role-header band.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class InfoTableUitkController : MonoBehaviour
    {
        private const string RootName = "info-table";
        private const string FrameClass = "info-table__frame";
        private const string HeaderRowClass = "info-table__header-row";
        private const string GridClass = "info-table__grid";
        private const string ScrollClass = "info-table__scroll";
        private const string PlayerRowClass = "info-table__player-row";
        private const string PlayerRowAltClass = "info-table__player-row--alt";
        private const string PlayerRowLockedClass = "info-table__player-row--locked";
        private const string CornerClass = "info-table__corner";
        private const string RoleHeadClass = "info-table__role-head";
        private const string NameCellClass = "info-table__name-cell";
        private const string NameLockedClass = "info-table__name-cell--locked";
        private const string CellClass = "info-table__cell";
        private const string CellConflictClass = "info-table__cell--conflict";
        private const string CellLockedClass = "info-table__cell--locked";
        private const string SegmentClass = "info-table__seg";
        private const string SegSureClass = "info-table__seg--sure";
        private const string SegMaybeClass = "info-table__seg--maybe";
        private const string SegNotClass = "info-table__seg--not";
        private const string SegSelectedClass = "info-table__seg--selected";
        private const string FooterClass = "info-table__footer";
        private const string BannerClass = "info-table__banner";
        private const string CapacityRowClass = "info-table__capacity-row";
        private const string ChipClass = "info-table__chip";
        private const string ChipOverClass = "info-table__chip--over";
        private const string ChipNameClass = "info-table__chip-name";
        private const string ChipCountClass = "info-table__chip-count";
        private const string LegendClass = "info-table__legend";
        private const string LegendItemClass = "info-table__legend-item";

        // Responsive tier classes stamped on the root (mutually exclusive per axis). USS does the rest.
        private const string PlayerComfortClass = "is-comfort";
        private const string PlayerCompactClass = "is-compact";
        private const string PlayerDenseClass = "is-dense";
        private const string PlayerScrollClass = "is-scroll";
        private const string RolesWideClass = "roles-wide";
        private const string RolesCompactClass = "roles-compact";
        private const string RolesIconClass = "roles-icon";

        // Player-count breakpoints: <7 comfort, 7-11 compact, 12-15 dense, >=16 scroll (rows go fixed + overflow).
        private const int CompactAtPlayers = 7;
        private const int DenseAtPlayers = 12;
        private const int ScrollAtPlayers = 16;
        // Role-count breakpoints: <6 wide labels, 6-8 compact, >=9 tiny + tooltip.
        private const int CompactAtRoles = 6;
        private const int IconAtRoles = 9;

        private const string GlyphSure = "✓";
        private const string GlyphMaybe = "?";
        private const string GlyphNot = "✗";
        private const string CollapsedClass = "cdp-is-collapsed";

        [SerializeField] private UIDocument _document;

        [Tooltip("A MonoBehaviour implementing IInfoTableDataSource (GameInfoTableDataSource / DemoInfoTableDataSource).")]
        [SerializeField] private MonoBehaviour _dataSourceBehaviour;

        private static readonly CellState[] SegmentOrder = { CellState.Sure, CellState.Maybe, CellState.SurelyNot };
        private static readonly string[] AllPlayerTierClasses =
            { PlayerComfortClass, PlayerCompactClass, PlayerDenseClass, PlayerScrollClass };
        private static readonly string[] AllRoleTierClasses =
            { RolesWideClass, RolesCompactClass, RolesIconClass };

        private IInfoTableDataSource _data;
        private readonly InfoTableModel _model = new();
        private VisualElement _root;
        private VisualElement[][] _cellEls;
        private VisualElement[] _rowEls;
        private VisualElement[] _nameEls;
        private VisualElement[] _capacityChips;
        private Label[] _capacityCounts;
        private Label _banner;
        private bool _initialized;

        private void OnEnable()
        {
            _data = _dataSourceBehaviour as IInfoTableDataSource;
            if (_data != null)
            {
                _data.OnRebuildRequested += Rebuild;
                _data.OnRevealChanged += ScanReveals;
            }
            _model.OnChanged += Render;
            TryInitialize();
        }

        // UIDocument builds its tree in its OWN OnEnable; order vs this component is not guaranteed, so retry at
        // Start (and build if the data source already fired its rebuild before the tree existed).
        private void Start()
        {
            TryInitialize();
            if (_root != null && _cellEls == null && _data != null) Rebuild();
        }

        private void OnDisable()
        {
            if (_data != null)
            {
                _data.OnRebuildRequested -= Rebuild;
                _data.OnRevealChanged -= ScanReveals;
            }
            _model.OnChanged -= Render;
        }

        private void TryInitialize()
        {
            if (_initialized) return;
            if (_document == null) _document = GetComponent<UIDocument>();

            VisualElement tree = _document != null ? _document.rootVisualElement : null;
            _root = tree?.Q<VisualElement>(RootName);
            if (_root == null) return;

            _initialized = true;
        }

        private void Rebuild()
        {
            TryInitialize();
            if (_root == null || _data == null) return;

            _model.Build(_data.GetPlayers(), _data.GetRoles());
            BuildGrid();
            ScanReveals();
        }

        private void BuildGrid()
        {
            int playerCount = _model.PlayerCount;
            int roleCount = _model.RoleCount;
            ApplyTierClasses(playerCount, roleCount);

            _root.Clear();
            _cellEls = new VisualElement[playerCount][];
            _rowEls = new VisualElement[playerCount];
            _nameEls = new VisualElement[playerCount];

            // Dossier frame: a gold-bordered card holding the frozen header band + the (scrolling or filling) body.
            var frame = new VisualElement { name = "frame" };
            frame.AddToClassList(FrameClass);

            // Frozen header band: corner + role headers. Stays put while the body scrolls.
            var header = new VisualElement { name = "header-row" };
            header.AddToClassList(HeaderRowClass);
            var corner = new Label("Joueurs / Rôles");
            corner.AddToClassList(CornerClass);
            header.Add(corner);
            for (int ri = 0; ri < roleCount; ri++)
            {
                InfoTableRole role = _model.Roles[ri];
                string text = role.Capacity > 1 ? $"{role.RoleName} ×{role.Capacity}" : role.RoleName;
                var roleHead = new Label(text);
                roleHead.AddToClassList(RoleHeadClass);
                roleHead.tooltip = text;   // full name on hover — survives the compact/icon role tiers
                header.Add(roleHead);
            }
            frame.Add(header);

            // Body: past the scroll breakpoint it is a vertical ScrollView (rows go fixed-height + overflow);
            // otherwise a plain column whose rows flex-grow to fill the tablet. The USS tier class flips the
            // row layout regime (flex-grow vs fixed height) — see the is-scroll rules.
            bool scroll = playerCount >= ScrollAtPlayers;
            VisualElement body;
            VisualElement rowParent;
            if (scroll)
            {
                var sv = new ScrollView(ScrollViewMode.Vertical) { name = "scroll" };
                sv.AddToClassList(ScrollClass);
                body = sv;
                rowParent = sv.contentContainer;
            }
            else
            {
                var grid = new VisualElement { name = "grid" };
                grid.AddToClassList(GridClass);
                body = grid;
                rowParent = grid;
            }

            for (int pi = 0; pi < playerCount; pi++)
            {
                var row = new VisualElement();
                row.AddToClassList(PlayerRowClass);
                if ((pi & 1) == 1) row.AddToClassList(PlayerRowAltClass);
                _rowEls[pi] = row;

                var name = new Label(_model.Players[pi].Pseudo);
                name.AddToClassList(NameCellClass);
                _nameEls[pi] = name;
                row.Add(name);

                _cellEls[pi] = new VisualElement[roleCount];
                for (int ri = 0; ri < roleCount; ri++)
                {
                    var cell = new VisualElement();
                    cell.AddToClassList(CellClass);
                    AddSegment(cell, pi, ri, CellState.Sure, SegSureClass, GlyphSure);
                    AddSegment(cell, pi, ri, CellState.Maybe, SegMaybeClass, GlyphMaybe);
                    AddSegment(cell, pi, ri, CellState.SurelyNot, SegNotClass, GlyphNot);
                    _cellEls[pi][ri] = cell;
                    row.Add(cell);
                }
                rowParent.Add(row);
            }
            frame.Add(body);
            _root.Add(frame);

            BuildFooter(roleCount);
            Render();
        }

        // Compute the responsive tier from the counts and stamp exactly one class per axis on the root.
        private void ApplyTierClasses(int playerCount, int roleCount)
        {
            foreach (string c in AllPlayerTierClasses) _root.RemoveFromClassList(c);
            foreach (string c in AllRoleTierClasses) _root.RemoveFromClassList(c);

            string playerTier =
                playerCount >= ScrollAtPlayers ? PlayerScrollClass :
                playerCount >= DenseAtPlayers ? PlayerDenseClass :
                playerCount >= CompactAtPlayers ? PlayerCompactClass :
                PlayerComfortClass;
            _root.AddToClassList(playerTier);

            string roleTier =
                roleCount >= IconAtRoles ? RolesIconClass :
                roleCount >= CompactAtRoles ? RolesCompactClass :
                RolesWideClass;
            _root.AddToClassList(roleTier);
        }

        // A single segment of a cell: a glyph tile, ghosted until selected. Clicking sets/toggles the state.
        private void AddSegment(VisualElement cell, int playerIndex, int roleIndex, CellState state, string colorClass, string glyph)
        {
            var seg = new Label(glyph);
            seg.AddToClassList(SegmentClass);
            seg.AddToClassList(colorClass);

            int pi = playerIndex;
            int ri = roleIndex;
            CellState s = state;
            seg.RegisterCallback<ClickEvent>(_ => _model.SetCell(pi, ri, s));

            cell.Add(seg);
        }

        // Footer "context bar": conflict banner + per-role capacity counters + a states legend.
        private void BuildFooter(int roleCount)
        {
            var footer = new VisualElement { name = "footer" };
            footer.AddToClassList(FooterClass);

            _banner = new Label("⚠  Contradiction dans tes déductions");
            _banner.AddToClassList(BannerClass);
            footer.Add(_banner);

            var capacityRow = new VisualElement { name = "capacity-row" };
            capacityRow.AddToClassList(CapacityRowClass);
            _capacityChips = new VisualElement[roleCount];
            _capacityCounts = new Label[roleCount];
            for (int ri = 0; ri < roleCount; ri++)
            {
                var chip = new VisualElement();
                chip.AddToClassList(ChipClass);

                var chipName = new Label(_model.Roles[ri].RoleName);
                chipName.AddToClassList(ChipNameClass);

                var chipCount = new Label($"0/{_model.Roles[ri].Capacity}");
                chipCount.AddToClassList(ChipCountClass);

                chip.Add(chipName);
                chip.Add(chipCount);
                capacityRow.Add(chip);
                _capacityChips[ri] = chip;
                _capacityCounts[ri] = chipCount;
            }
            footer.Add(capacityRow);

            var legend = new VisualElement { name = "legend" };
            legend.AddToClassList(LegendClass);
            legend.Add(BuildLegendItem(SegSureClass, GlyphSure, "Sûr"));
            legend.Add(BuildLegendItem(SegMaybeClass, GlyphMaybe, "Je pense"));
            legend.Add(BuildLegendItem(SegNotClass, GlyphNot, "Pas lui"));
            footer.Add(legend);

            _root.Add(footer);
        }

        private VisualElement BuildLegendItem(string colorClass, string glyph, string label)
        {
            var item = new VisualElement();
            item.AddToClassList(LegendItemClass);

            var mark = new Label(glyph);
            mark.AddToClassList(SegmentClass);
            mark.AddToClassList(colorClass);
            mark.AddToClassList(SegSelectedClass);   // legend marks always read as "on"
            mark.pickingMode = PickingMode.Ignore;

            var text = new Label(label);

            item.Add(mark);
            item.Add(text);
            return item;
        }

        // Re-scan the data source: lock any row whose player's role is now revealed.
        private void ScanReveals()
        {
            if (_data == null || _cellEls == null) return;

            var players = _model.Players;
            for (int pi = 0; pi < players.Count; pi++)
            {
                if (_model.IsLocked(pi)) continue;
                string revealed = _data.GetRevealedRoleName(players[pi].ClientId);
                if (!string.IsNullOrEmpty(revealed)) _model.LockRowToRole(pi, revealed);
            }
        }

        private void Render()
        {
            if (_cellEls == null) return;

            for (int pi = 0; pi < _cellEls.Length; pi++)
            {
                bool locked = _model.IsLocked(pi);
                _rowEls[pi].EnableInClassList(PlayerRowLockedClass, locked);
                _nameEls[pi].EnableInClassList(NameLockedClass, locked);

                VisualElement[] cells = _cellEls[pi];
                for (int ri = 0; ri < cells.Length; ri++)
                {
                    VisualElement cell = cells[ri];
                    CellState state = _model.GetCell(pi, ri);

                    cell.EnableInClassList(CellLockedClass, locked);
                    cell.EnableInClassList(CellConflictClass, _model.IsCellInConflict(pi, ri));

                    int idx = 0;
                    foreach (VisualElement seg in cell.Children())
                    {
                        bool selected = idx < SegmentOrder.Length && state == SegmentOrder[idx];
                        seg.EnableInClassList(SegSelectedClass, selected);
                        idx++;
                    }
                }
            }

            RenderFooter();
        }

        private void RenderFooter()
        {
            if (_capacityChips != null)
            {
                for (int ri = 0; ri < _capacityChips.Length; ri++)
                {
                    int sure = _model.GetSureCountForRole(ri);
                    _capacityCounts[ri].text = $"{sure}/{_model.Roles[ri].Capacity}";
                    _capacityChips[ri].EnableInClassList(ChipOverClass, _model.IsRoleOverCapacity(ri));
                }
            }
            if (_banner != null) _banner.EnableInClassList(CollapsedClass, !_model.HasAnyConflict());
        }
    }
}
