# Wiring guide — flat screens (Login / MainMenu / LobbyBrowser)

Do this in the **Unity MCP session** (editor required). Goal: turn the front-loaded `.uxml`/`.uss`
(`Assets/UI Toolkit/Screens/`) into functional screens **without changing any game/network logic** — only
the view layer (uGUI refs → UITK queries) changes. Compile (`read_console`) + play-test after each.

> Authored without a running editor: treat the C# below as a precise recipe to paste & adapt, not
> compiler-verified code. Element names match the UXML exactly.

---

## 0. General porting recipe (applies to all three)

**Asset setup (once):**
1. `Assets > Create > UI Toolkit > Panel Settings` → **Render Mode = Screen Space Overlay**. One shared
   asset is fine; set its **Sort Order** above/below the remaining uGUI Canvas so they layer correctly
   during the transition.
2. On the screen's GameObject, add a **UI Document** (Source Asset = the screen `.uxml`, Panel Settings =
   the asset above). Keep the existing controller component on the same GameObject.

**Controller port pattern:** keep the class, its base type (MonoBehaviour/NetworkBehaviour), and **all**
async/network methods. Replace only the `[SerializeField]` uGUI fields + `Start()` listener hookups:

```csharp
private UIDocument _doc;
private bool _bound;

private void OnEnable() => TryBind();
private void Update() { if (!_bound) TryBind(); }   // UIDocument may build its tree after our OnEnable

private void TryBind()
{
    _doc = GetComponent<UIDocument>();
    VisualElement _root = _doc != null ? _doc.rootVisualElement : null;
    if (_root == null || _root.childCount == 0) return;   // not ready yet

    // ... Q<>() lookups + callback hookups (see each screen below) ...

    _bound = true;
}
```

**uGUI → UITK cheat-sheet:**

| uGUI | UITK |
|---|---|
| `btn.onClick.AddListener(M)` | `root.Q<Button>("id").clicked += M` |
| `TMP_InputField.text` | `TextField.value` |
| `TMP_InputField.onValueChanged.AddListener(M)` | `tf.RegisterValueChangedCallback(e => M(e.newValue))` |
| `TMP_Text.text = s` | `Label.text = s` |
| `btn.interactable = b` | `element.SetEnabled(b)` |
| `canvasGroup.DoShowGroup()` / `DoHideGroup()` | `element.style.display = DisplayStyle.Flex / None` |
| `Slider` (`onValueChanged`, `SetValueWithoutNotify`) | `SliderInt` (`RegisterValueChangedCallback`, `SetValueWithoutNotify`) |
| `Instantiate(rowPrefab, content)` | `ListView` (`makeItem`/`bindItem`/`itemsSource`) or `templateContainer.Add(clone)` |
| `GraphicRaycaster` + `EventSystem` | built into the screen-space panel (nothing to add) |

`using UnityEngine.UIElements;` everywhere below.

---

## 1. Login  (`LoginMenu.cs` → `Screens/Login.uxml`)

Elements: `field-pseudo` (TextField), `btn-login` (Button), `error-text` (Label),
`login-panel` (VisualElement), `overlay-loading` (VisualElement).

Replace `usernameInputField` / `loginButton` / `errorText` / the three CanvasGroups:

```csharp
_pseudo   = _root.Q<TextField>("field-pseudo");
_loginBtn = _root.Q<Button>("btn-login");
_errorLbl = _root.Q<Label>("error-text");
_panel    = _root.Q<VisualElement>("login-panel");
_loading  = _root.Q<VisualElement>("overlay-loading");

_loginBtn.clicked += () => _ = OnSignInButtonClicked();
```

Then, in the existing methods, swap only the view reads/writes (logic untouched):
- `usernameInputField.text`  → `_pseudo.value`  (Steam path: `_pseudo.value = SteamClient.Name;`)
- `ShowLogin()`   → `_panel.style.display = DisplayStyle.Flex;  _loading.style.display = DisplayStyle.None;`
- `ShowLoading()` → `_panel.style.display = DisplayStyle.None;  _loading.style.display = DisplayStyle.Flex;`
- `ShowMainMenu()` → cross-document now (see §4): the main menu is a separate UIDocument, not `menuCanvasGroup`.
- `DisplayError(msg)` (DOTween fade has no VisualElement equivalent) →
  add `transition-property: opacity; transition-duration: 1s;` to `.login-error` in `Login.uss`, then:
  ```csharp
  _errorLbl.text = msg;
  _errorLbl.style.opacity = 1f;
  _errorLbl.schedule.Execute(() => _errorLbl.style.opacity = 0f).StartingIn(3000);
  ```

Keep UGS init, Steam auto-login, `SignIn`/`SignInWithSteam`/`SanitizeUsername` exactly as-is.

---

## 2. MainMenu  (`UI.MainMenu` → `Screens/MainMenu.uxml`)

Elements: screens `screen-main`/`screen-host`/`screen-join`, overlay `overlay-loading`; buttons
`btn-show-host`, `btn-browse`, `btn-show-join`, `btn-quit`, `btn-host-back`, `btn-host`, `btn-join-back`,
`btn-join`; fields `field-lobby-name`, `field-password`, `field-join-code`; label `host-hint`.

```csharp
_screenMain = _root.Q<VisualElement>("screen-main");
_screenHost = _root.Q<VisualElement>("screen-host");
_screenJoin = _root.Q<VisualElement>("screen-join");
_loading    = _root.Q<VisualElement>("overlay-loading");

_lobbyName  = _root.Q<TextField>("field-lobby-name");
_password   = _root.Q<TextField>("field-password");
_joinCode   = _root.Q<TextField>("field-join-code");
_hostBtn    = _root.Q<Button>("btn-host");
_hostHint   = _root.Q<Label>("host-hint");

_root.Q<Button>("btn-show-host").clicked += () => ShowScreen(_screenHost);   // was OnOpenHostButtonClicked
_root.Q<Button>("btn-show-join").clicked += () => ShowScreen(_screenJoin);   // was OnJoinMenuButtonClicked
_root.Q<Button>("btn-browse").clicked    += OpenLobbyBrowser;                // see §4 (was the separate browser)
_root.Q<Button>("btn-host-back").clicked += () => ShowScreen(_screenMain);   // was OnCloseHostButtonClicked
_root.Q<Button>("btn-join-back").clicked += () => ShowScreen(_screenMain);   // was OnCloseJoinMenuButtonClicked
_root.Q<Button>("btn-quit").clicked      += OnQuitGameButtonClicked;
_hostBtn.clicked                         += OnHostButtonClicked;
_root.Q<Button>("btn-join").clicked      += OnJoinButtonClicked;

_lobbyName.RegisterValueChangedCallback(e => { OnLobbyNameValueChange(e.newValue); ValidateHostName(e.newValue); });
ValidateHostName(_lobbyName.value);
```

Helpers + value swaps:
- `ShowScreen(target)`: set `display` Flex on target, None on the other two (screen-host/join are absolute
  overlays — already styled via `.menu-sub`).
- `OnHostButtonClickedAsync` / `OnJoinButtonClickedAsync`: replace
  `lobbyNameInputField.text`→`_lobbyName.value`, `lobbyPasswordInputField.text`→`_password.value`,
  `joinCodeInputField.text`→`_joinCode.value`; `loadingCanvasGroup.DoShowGroup/Hide`→`_loading.style.display`;
  `_hostButton.interactable=false`→`_hostBtn.SetEnabled(false)`.
- `ValidateHostName(name)` (new niceness, replaces the silent early-return): `bool ok = name?.Length >= 3;
  _hostBtn.SetEnabled(ok); _hostHint.style.display = ok ? None : Flex;`

Keep **all** of `HostWithFacepunch`/`HostWithUnityRelay`/`JoinWithFacepunch`/`JoinWithUnityRelay`,
`LobbyManager`, `GameCode`, `SwitchToGameScene`, and the `_isBusy` guard untouched.

---

## 3. LobbyBrowser  (`UI.Lobby.LobbySelectionPanel` → `Screens/LobbyBrowser.uxml`)

Elements: `lobby-list` (ScrollView — swap to `ListView`, see below), `btn-refresh`, `btn-connect`,
`btn-back`; modal `modal-password` (+ `field-join-password`, `btn-submit-password`, `btn-cancel-password`);
overlay `overlay-loading`.

**Row list — replace prefab instantiation with a `ListView`** (drop the `lobby-list` ScrollView, add a
`<ui:ListView name="lobby-list" .../>` in the UXML, or keep the name and swap the type). This deletes the
whole `CreateEntry` + header-width-sync + `existingEntries` bookkeeping (a real UITK win — the columns
align via USS):

```csharp
_list = _root.Q<ListView>("lobby-list");
_list.fixedItemHeight = 52;
_list.selectionType = SelectionType.Single;
_list.itemsSource = _lobbies;                      // List<Unity.Services.Lobbies.Models.Lobby>
_list.makeItem = () => {
    var r = new VisualElement(); r.AddToClassList("lobby-row");
    r.Add(NewLabel("lcol","lcol-name")); r.Add(NewLabel("lcol","lcol-priv"));
    r.Add(NewLabel("lcol","lcol-lang")); r.Add(NewLabel("lcol","lcol-players"));
    return r;
};
_list.bindItem = (e, i) => {
    var l = _lobbies[i];
    (e[0] as Label).text = l.Name;
    (e[1] as Label).text = l.HasPassword ? "Oui" : "Non";
    (e[2] as Label).text = ReadLanguage(l);        // existing Language-data + display-name lookup
    (e[3] as Label).text = $"{l.Players.Count}/{l.MaxPlayers}";
};
_list.selectionChanged += _ => {
    currentLobbySelected = _list.selectedIndex >= 0 ? _lobbies[_list.selectedIndex] : null;
    _connectBtn.SetEnabled(currentLobbySelected != null);
};                                                  // replaces OnSelectedFeedback/OnDeselectedFeedback (DOColor)
```

In `RefreshAsync`: keep `LobbyManager.instance.GetLobbies()`, filter `IsLocked`, then
`_lobbies.Clear(); _lobbies.AddRange(result); _list.RefreshItems();` (replaces the per-entry diff/destroy).

Buttons / modal / overlay:
- `refreshButton`→`btn-refresh` (`Refresh`); `connectButton`→`btn-connect` (`OnConnectButtonClicked`);
  add `btn-back` → §4.
- `enterPasswordCanvasGroup`→`modal-password` (`.style.display`); `passwordInputField`→`field-join-password`
  (`.value`); `submitPasswordButton`→`btn-submit-password`; `cancelPasswordButton`→`btn-cancel-password`.
- `loadingCanvasGroup`→`overlay-loading`.
- 7s auto-refresh: keep the `Update` timer, or `_root.schedule.Execute(Refresh).Every(7000);` and drop `Update`.

Keep `JoinLobby`/`JoinWithFacepunch`/`JoinWithUnityRelay`, the `_isRefreshing`/`_isJoining` guards,
`GameCode`, and scene load untouched.

---

## 4. Navigation between screens

uGUI toggled CanvasGroups inside one prefab; now each screen is its own UIDocument. Pick one:
- **Simplest:** keep all three UIDocuments under one root GameObject and enable/disable the GameObjects
  (or their `UIDocument.rootVisualElement.style.display`) on navigation.
- Or a tiny `MenuRouter` that holds refs to the three UIDocuments and shows one at a time.

`Login.ShowMainMenu()` → show MainMenu doc, hide Login doc. `MainMenu.OpenLobbyBrowser()` → show
LobbyBrowser doc. `btn-back` on the browser → back to MainMenu.

---

## 5. After each screen verifies
`read_console` (zero errors) → play-test the full flow incl. the networked paths (host, join-by-code,
browse+join, password) → only THEN delete the old uGUI Canvas/prefab for that screen.
