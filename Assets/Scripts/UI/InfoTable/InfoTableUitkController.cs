using UnityEngine;
using UnityEngine.UIElements;

namespace UI.InfoTable
{
    /// <summary>
    /// Drives the UITK deduction board (InfoTable.uxml). Builds a Players×Roles grid from an
    /// <see cref="IInfoTableDataSource"/>, cycles cell state on swatch clicks through an <see cref="InfoTableModel"/>,
    /// and re-renders conflict/lock visuals. The UITK replacement for the retired uGUI <c>InfoTableSystem</c>;
    /// follows the <c>RoleCardController</c> conventions (guarded init, Q-by-name, dynamic children, BEM classes).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class InfoTableUitkController : MonoBehaviour
    {
        private const string RootName = "info-table";
        private const string HeaderRowClass = "info-table__header-row";
        private const string PlayerRowClass = "info-table__player-row";
        private const string CornerClass = "info-table__corner";
        private const string RoleHeadClass = "info-table__role-head";
        private const string NameCellClass = "info-table__name-cell";
        private const string NameLockedClass = "info-table__name-cell--locked";
        private const string CellClass = "info-table__cell";
        private const string CellConflictClass = "info-table__cell--conflict";
        private const string CellLockedClass = "info-table__cell--locked";
        private const string SwatchClass = "info-table__swatch";
        private const string SwatchSureClass = "info-table__swatch--sure";
        private const string SwatchMaybeClass = "info-table__swatch--maybe";
        private const string SwatchNotClass = "info-table__swatch--not";
        private const string SwatchSelectedClass = "info-table__swatch--selected";
        private const string SwatchBoxClass = "info-table__swatch-box";
        private const string CheckClass = "info-table__check";

        [SerializeField] private UIDocument _document;

        [Tooltip("A MonoBehaviour implementing IInfoTableDataSource (GameInfoTableDataSource / DemoInfoTableDataSource).")]
        [SerializeField] private MonoBehaviour _dataSourceBehaviour;

        private static readonly CellState[] SwatchOrder = { CellState.Sure, CellState.Maybe, CellState.SurelyNot };

        private IInfoTableDataSource _data;
        private readonly InfoTableModel _model = new();
        private VisualElement _root;
        private VisualElement[][] _cellEls;
        private VisualElement[] _nameEls;
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

        // UIDocument builds its tree in its OWN OnEnable; order vs this component is not guaranteed, so retry
        // at Start (and build if the data source already fired its rebuild before the tree existed).
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
            _root.Clear();
            int playerCount = _model.PlayerCount;
            int roleCount = _model.RoleCount;
            _cellEls = new VisualElement[playerCount][];
            _nameEls = new VisualElement[playerCount];

            // Header row: corner label + one header per role (capacity suffix when >1).
            var header = new VisualElement { name = "header-row" };
            header.AddToClassList(HeaderRowClass);
            var corner = new Label("Joueurs / Rôles");
            corner.AddToClassList(CornerClass);
            header.Add(corner);
            for (int ri = 0; ri < roleCount; ri++)
            {
                InfoTableRole role = _model.Roles[ri];
                string text = role.Capacity > 1 ? $"{role.RoleName} *{role.Capacity}" : role.RoleName;
                var roleHead = new Label(text);
                roleHead.AddToClassList(RoleHeadClass);
                header.Add(roleHead);
            }
            _root.Add(header);

            // One row per player: name cell + one cell per role.
            for (int pi = 0; pi < playerCount; pi++)
            {
                var row = new VisualElement();
                row.AddToClassList(PlayerRowClass);

                var name = new Label(_model.Players[pi].Pseudo);
                name.AddToClassList(NameCellClass);
                _nameEls[pi] = name;
                row.Add(name);

                _cellEls[pi] = new VisualElement[roleCount];
                for (int ri = 0; ri < roleCount; ri++)
                {
                    var cell = new VisualElement();
                    cell.AddToClassList(CellClass);
                    AddSwatch(cell, pi, ri, CellState.Sure, SwatchSureClass);
                    AddSwatch(cell, pi, ri, CellState.Maybe, SwatchMaybeClass);
                    AddSwatch(cell, pi, ri, CellState.SurelyNot, SwatchNotClass);
                    _cellEls[pi][ri] = cell;
                    row.Add(cell);
                }
                _root.Add(row);
            }

            Render();
        }

        private void AddSwatch(VisualElement cell, int playerIndex, int roleIndex, CellState state, string colorClass)
        {
            var swatch = new VisualElement();
            swatch.AddToClassList(SwatchClass);
            swatch.AddToClassList(colorClass);

            var box = new VisualElement();
            box.AddToClassList(SwatchBoxClass);
            swatch.Add(box);

            var check = new VisualElement();
            check.AddToClassList(CheckClass);
            swatch.Add(check);

            int pi = playerIndex;
            int ri = roleIndex;
            CellState s = state;
            swatch.RegisterCallback<ClickEvent>(_ => _model.SetCell(pi, ri, s));

            cell.Add(swatch);
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
                _nameEls[pi].EnableInClassList(NameLockedClass, locked);

                VisualElement[] cells = _cellEls[pi];
                for (int ri = 0; ri < cells.Length; ri++)
                {
                    VisualElement cell = cells[ri];
                    CellState state = _model.GetCell(pi, ri);

                    cell.EnableInClassList(CellLockedClass, locked);
                    cell.EnableInClassList(CellConflictClass, _model.IsCellInConflict(pi, ri));

                    int idx = 0;
                    foreach (VisualElement swatch in cell.Children())
                    {
                        bool selected = idx < SwatchOrder.Length && state == SwatchOrder[idx];
                        swatch.EnableInClassList(SwatchSelectedClass, selected);
                        idx++;
                    }
                }
            }
        }
    }
}
