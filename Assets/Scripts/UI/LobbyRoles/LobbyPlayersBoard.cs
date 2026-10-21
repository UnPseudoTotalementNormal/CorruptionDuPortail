using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameLogic;
using Network.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI.LobbyRoles
{
    /// <summary>
    /// The lobby's players, on a small cork board at the left of the screen where the old "Connected Players" panel
    /// stood (mockup tools/maquettes/attribution-roles v9 J1, kraft of v10 K1): one kraft label pinned per player, its
    /// pseudo, and a red tick once that player is ready. Read-only: nothing on it takes a click (root and children
    /// pickingMode=Ignore), so it never steals the tablet's pointer.
    ///
    /// Source = the replicated player census (<c>LobbyPlayerInfoHolder</c>, same as the tablet's
    /// <see cref="GameLobbyRolesDataSource"/>), resolved through the CompositionRoot; rebuilt on every roster change,
    /// which includes a player's ready flag.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class LobbyPlayersBoard : MonoBehaviour
    {
        private const string RootName = "lobby-players";
        private const string TagClass = "lobby-players__tag";
        private const string NameClass = "lobby-players__name";
        private const string TickClass = "lobby-players__tick";

        // Scatter keys of the labels (PinnedPaper): offset by the client id so a label keeps its look when another
        // player leaves, and stays clear of the tablet's own keys.
        private const int KeyBase = 1000;

        [SerializeField] private UIDocument _document;

        private Network.LobbyPlayerInfoHolder _lobby;
        // The lobby screen's CanvasGroup (LobbyUI): a state UI hides by fading it to 0, which a UIDocument ignores, so
        // the board copies its alpha (it stayed on screen for the whole game otherwise).
        private CanvasGroup _screenGroup;
        private float _shownAlpha = -1f;
        private VisualElement _shownRoot;

        private void Start()
        {
            if (_document == null) _document = GetComponent<UIDocument>();
            _screenGroup = GetComponentInParent<CanvasGroup>(true);
            ResolveWhenReadyAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private void LateUpdate()
        {
            VisualElement docRoot = _document != null ? _document.rootVisualElement : null;
            if (docRoot == null) return;
            float alpha = _screenGroup != null ? _screenGroup.alpha : 1f;
            // A re-enabled UIDocument builds a new root: apply again then.
            if (docRoot == _shownRoot && Mathf.Approximately(alpha, _shownAlpha)) return;
            _shownRoot = docRoot;
            _shownAlpha = alpha;
            docRoot.style.opacity = alpha;
            docRoot.style.display = alpha > 0.001f ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private async UniTaskVoid ResolveWhenReadyAsync(CancellationToken _token)
        {
            await UniTask.WaitUntil(
                () => NetworkManager.Singleton != null
                      && CompositionRoot.For(NetworkManager.Singleton).LobbyPlayerInfoHolder != null
                      && CompositionRoot.For(NetworkManager.Singleton).LobbyPlayerInfoHolder.playerInfos != null,
                cancellationToken: _token);

            if (_token.IsCancellationRequested) return;

            _lobby = CompositionRoot.For(NetworkManager.Singleton).LobbyPlayerInfoHolder;
            _lobby.onRosterChanged += Rebuild;
            Rebuild();
        }

        private void OnEnable() => Rebuild();

        private void OnDestroy()
        {
            if (_lobby != null) _lobby.onRosterChanged -= Rebuild;
        }

        /// <summary>One label to show: the pseudo as every name surface shows it, ready or not, and the client id that keys its look.</summary>
        internal readonly struct Entry
        {
            public readonly string Pseudo;
            public readonly bool Ready;
            public readonly ulong ClientId;

            public Entry(string pseudo, bool ready, ulong clientId)
            {
                Pseudo = pseudo;
                Ready = ready;
                ClientId = clientId;
            }
        }

        private void Rebuild()
        {
            // Unity-safe null check: a destroyed holder is not C#-null (teardown).
            if (_lobby == null) return;
            var entries = new List<Entry>();
            foreach (PlayerInfo info in _lobby.playerInfos)
            {
                if (info.hasLeft) continue;
                entries.Add(new Entry(_lobby.GetDisplayPseudo(info.playerClientId), info.isReady, info.playerClientId));
            }
            Show(entries);
        }

        // Network-free, so the board can be rendered offscreen from the editor with sample players.
        internal void Show(IReadOnlyList<Entry> entries)
        {
            if (_document == null) return;
            VisualElement docRoot = _document.rootVisualElement;
            VisualElement root = docRoot != null ? docRoot.Q<VisualElement>(RootName) : null;
            if (root == null) return;

            docRoot.pickingMode = PickingMode.Ignore;
            root.pickingMode = PickingMode.Ignore;
            root.Clear();
            foreach (Entry entry in entries) root.Add(BuildTag(entry));
        }

        private static VisualElement BuildTag(Entry entry)
        {
            int key = KeyBase + (int)(entry.ClientId % 100000);

            var tag = new VisualElement { pickingMode = PickingMode.Ignore };
            tag.AddToClassList(TagClass);
            var name = new Label(entry.Pseudo) { pickingMode = PickingMode.Ignore };
            name.AddToClassList(NameClass);
            tag.Add(name);
            if (entry.Ready)
            {
                var tick = new VisualElement { pickingMode = PickingMode.Ignore };
                tick.AddToClassList(TickClass);
                tag.Add(tick);
            }
            PinnedPaper.Pin(tag, PinnedPaper.PinX(key));
            PinnedPaper.Rest(tag, key);
            return tag;
        }
    }
}
