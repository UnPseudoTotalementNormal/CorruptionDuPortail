using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spikes.MenuPilot
{
    /// <summary>
    /// SPIKE B (issue #63) — THROWAWAY flat-screen pilot. A clickable UI Toolkit rebuild of the Main Menu +
    /// Lobby browser, mirroring the EXISTING uGUI flow (<c>UI.MainMenu</c>, <c>UI.Lobby.LobbySelectionPanel</c>,
    /// <c>UI.LobbyUI.LobbyEntryUI</c>) so Poyo can judge, hands-on, whether AI-authored UXML/USS is a real
    /// upgrade over the current Canvas workflow.
    ///
    /// Self-contained: own asmdef, NO dependency on the Game assembly and NO live Facepunch / Unity Lobbies
    /// calls. The lobby list is MOCK data so it runs in any scene without Steam, auth, or the network stack.
    /// The point is the look + the iteration feel, not the backend — wiring these screens to the real
    /// LobbyManager later is mechanical.
    ///
    /// Demonstrates the parts where UITK beats uGUI for this project: a data-driven <see cref="ListView"/>
    /// (the lobby table — same shape as InfoTable/TableSystem, the #63 sweet spot), USS transitions and
    /// hover/selection states, flexbox layout, and the UITK scheduler for the fake async loading overlay.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MenuPilotController : MonoBehaviour
    {
        [Tooltip("Optional: assign MenuPilot.uss here if it does not auto-load via the UXML <Style src>.")]
        [SerializeField] private StyleSheet _styleSheet;

        private UIDocument _doc;
        private bool _bound;

        // Screens / overlays.
        private VisualElement _screenMain;
        private VisualElement _screenHost;
        private VisualElement _screenBrowser;
        private VisualElement _modalPassword;
        private VisualElement _overlayLoading;
        private Label _loadingLabel;

        // Host form.
        private TextField _lobbyName;
        private Button _hostButton;
        private Label _hostHint;

        // Browser.
        private ListView _lobbyList;
        private Button _connectButton;

        private readonly List<MockLobby> _lobbies = new();
        private MockLobby _selected;

        private void OnEnable() => TryBind();

        private void Update()
        {
            // The UIDocument may build its tree after this component's OnEnable — retry until ready.
            if (!_bound)
            {
                TryBind();
            }
        }

        private void TryBind()
        {
            _doc = GetComponent<UIDocument>();
            VisualElement _root = _doc != null ? _doc.rootVisualElement : null;
            if (_root == null || _root.childCount == 0)
            {
                return;
            }

            if (_styleSheet != null && !_root.styleSheets.Contains(_styleSheet))
            {
                _root.styleSheets.Add(_styleSheet);
            }

            _screenMain = _root.Q<VisualElement>("screen-main");
            _screenHost = _root.Q<VisualElement>("screen-host");
            _screenBrowser = _root.Q<VisualElement>("screen-browser");
            _modalPassword = _root.Q<VisualElement>("modal-password");
            _overlayLoading = _root.Q<VisualElement>("overlay-loading");
            _loadingLabel = _root.Q<Label>("loading-label");

            // If any critical node is missing the tree is not ready yet — bail and let Update retry.
            if (_screenMain == null || _screenHost == null || _screenBrowser == null)
            {
                return;
            }

            BindMainMenu(_root);
            BindHostScreen(_root);
            BindBrowserScreen(_root);
            BindPasswordModal(_root);

            _bound = true;
            ShowScreen(_screenMain);
            HideOverlays();
        }

        // ---- Main menu ----------------------------------------------------------------------------------

        private void BindMainMenu(VisualElement _root)
        {
            _root.Q<Button>("btn-show-host").clicked += () => ShowScreen(_screenHost);
            _root.Q<Button>("btn-show-browser").clicked += () =>
            {
                PopulateLobbies();
                ShowScreen(_screenBrowser);
            };
            _root.Q<Button>("btn-settings").clicked += () => Debug.Log("[Menu Pilot] Paramètres — placeholder.");
            _root.Q<Button>("btn-quit").clicked += Quit;
        }

        // ---- Host screen --------------------------------------------------------------------------------

        private void BindHostScreen(VisualElement _root)
        {
            _lobbyName = _root.Q<TextField>("field-lobby-name");
            _hostButton = _root.Q<Button>("btn-host");
            _hostHint = _root.Q<Label>("host-hint");
            _root.Q<Button>("btn-host-back").clicked += () => ShowScreen(_screenMain);

            // Mirror MainMenu.OnHostButtonClicked: name must be >= 3 chars.
            _lobbyName.RegisterValueChangedCallback(_evt => ValidateHostName(_evt.newValue));
            _root.Q<Button>("btn-host").clicked += () =>
            {
                ShowLoading($"Création du salon « {_lobbyName.value} »…");
            };
            ValidateHostName(_lobbyName.value);
        }

        private void ValidateHostName(string _name)
        {
            bool _ok = !string.IsNullOrEmpty(_name) && _name.Length >= 3;
            _hostButton.SetEnabled(_ok);
            _hostHint.style.display = _ok ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ---- Browser screen -----------------------------------------------------------------------------

        private void BindBrowserScreen(VisualElement _root)
        {
            _lobbyList = _root.Q<ListView>("lobby-list");
            _connectButton = _root.Q<Button>("btn-connect");
            _root.Q<Button>("btn-browser-back").clicked += () => ShowScreen(_screenMain);
            _root.Q<Button>("btn-refresh").clicked += RefreshLobbies;
            _connectButton.clicked += OnConnectClicked;

            _lobbyList.fixedItemHeight = 56;
            _lobbyList.selectionType = SelectionType.Single;
            _lobbyList.itemsSource = _lobbies;
            _lobbyList.makeItem = MakeRow;
            _lobbyList.bindItem = BindRow;
            _lobbyList.selectionChanged += _ =>
            {
                _selected = _lobbyList.selectedIndex >= 0 ? _lobbies[_lobbyList.selectedIndex] : null;
                _connectButton.SetEnabled(_selected != null);
            };
            _connectButton.SetEnabled(false);
        }

        private static VisualElement MakeRow()
        {
            VisualElement _row = new() { name = "lobby-row" };
            _row.AddToClassList("lobby-row");
            _row.Add(NewCol("col-name", "col", "col-name"));
            _row.Add(NewCol("col-priv", "col", "col-priv"));
            _row.Add(NewCol("col-lang", "col", "col-lang"));
            _row.Add(NewCol("col-players", "col", "col-players"));
            return _row;
        }

        private static Label NewCol(string _name, params string[] _classes)
        {
            Label _l = new() { name = _name };
            foreach (string _c in _classes)
            {
                _l.AddToClassList(_c);
            }
            return _l;
        }

        private void BindRow(VisualElement _row, int _index)
        {
            MockLobby _l = _lobbies[_index];
            _row.Q<Label>("col-name").text = _l.Name;
            _row.Q<Label>("col-priv").text = _l.HasPassword ? "Oui" : "Non";
            _row.Q<Label>("col-lang").text = _l.Language;
            _row.Q<Label>("col-players").text = $"{_l.Players}/{_l.MaxPlayers}";
        }

        private void PopulateLobbies()
        {
            if (_lobbies.Count == 0)
            {
                _lobbies.AddRange(BuildMockLobbies());
            }
            _selected = null;
            _connectButton?.SetEnabled(false);
            _lobbyList?.ClearSelection();
            _lobbyList?.Rebuild();
        }

        // Simulate LobbySelectionPanel's 7s auto-refresh: jitter the player counts so the list feels live.
        private void RefreshLobbies()
        {
            foreach (MockLobby _l in _lobbies)
            {
                _l.Players = Mathf.Clamp(_l.Players + Random.Range(-2, 3), 1, _l.MaxPlayers);
            }
            _lobbyList?.RefreshItems();
        }

        private void OnConnectClicked()
        {
            if (_selected == null)
            {
                return;
            }
            if (_selected.HasPassword)
            {
                _modalPassword.style.display = DisplayStyle.Flex;
                return;
            }
            ShowLoading($"Connexion à « {_selected.Name} »…");
        }

        // ---- Password modal -----------------------------------------------------------------------------

        private void BindPasswordModal(VisualElement _root)
        {
            TextField _pwd = _root.Q<TextField>("field-join-password");
            _root.Q<Button>("btn-cancel-password").clicked += () =>
            {
                _pwd.value = string.Empty;
                _modalPassword.style.display = DisplayStyle.None;
            };
            _root.Q<Button>("btn-submit-password").clicked += () =>
            {
                if (string.IsNullOrEmpty(_pwd.value))
                {
                    return;
                }
                _modalPassword.style.display = DisplayStyle.None;
                ShowLoading($"Connexion à « {_selected?.Name} »…");
                _pwd.value = string.Empty;
            };
        }

        // ---- Shared helpers -----------------------------------------------------------------------------

        private void ShowScreen(VisualElement _target)
        {
            _screenMain.style.display = _target == _screenMain ? DisplayStyle.Flex : DisplayStyle.None;
            _screenHost.style.display = _target == _screenHost ? DisplayStyle.Flex : DisplayStyle.None;
            _screenBrowser.style.display = _target == _screenBrowser ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void HideOverlays()
        {
            if (_modalPassword != null)
            {
                _modalPassword.style.display = DisplayStyle.None;
            }
            if (_overlayLoading != null)
            {
                _overlayLoading.style.display = DisplayStyle.None;
            }
        }

        // Fake async: show the overlay, then auto-dismiss via the UITK scheduler (no coroutine / UniTask).
        private void ShowLoading(string _message)
        {
            if (_overlayLoading == null)
            {
                return;
            }
            if (_loadingLabel != null)
            {
                _loadingLabel.text = _message;
            }
            _overlayLoading.style.display = DisplayStyle.Flex;
            _overlayLoading.schedule.Execute(() => _overlayLoading.style.display = DisplayStyle.None).StartingIn(1400);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            Debug.Log("[Menu Pilot] Quitter — no-op en éditeur.");
#else
            Application.Quit();
#endif
        }

        // ---- Mock data ----------------------------------------------------------------------------------

        private sealed class MockLobby
        {
            public readonly string Name;
            public readonly bool HasPassword;
            public readonly string Language;
            public int Players;
            public readonly int MaxPlayers;

            public MockLobby(string _name, bool _hasPassword, string _language, int _players, int _maxPlayers)
            {
                Name = _name;
                HasPassword = _hasPassword;
                Language = _language;
                Players = _players;
                MaxPlayers = _maxPlayers;
            }
        }

        private static IEnumerable<MockLobby> BuildMockLobbies() => new List<MockLobby>
        {
            new("Le Portail Maudit", false, "Français", 7, 15),
            new("Taverne des Ombres", true, "Français", 12, 15),
            new("Cercle des Corrompus", false, "Français", 4, 10),
            new("Les Veilleurs du Néant", false, "Français", 9, 15),
            new("Sanctuaire Brisé", true, "Français", 2, 8),
            new("La Dernière Aube", false, "Français", 14, 15),
            new("Confrérie du Voile", false, "Français", 5, 12),
            new("Échos de la Corruption", true, "Français", 11, 15),
        };
    }
}
