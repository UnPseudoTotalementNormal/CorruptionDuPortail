#region

using Diagnostics.Logs;
using Network.Services;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#endregion

namespace Network.Steam
{
    /// <summary>
    /// Steam build, game lobby: "Inviter des amis" opens the Steam overlay's invite dialog for the current lobby. A
    /// friend who accepts lands in this lobby through <see cref="SteamInvites"/>. Built at runtime from the lobby's
    /// StartGame button, under the game code: same look, no prefab edit (visuals are placeholders, design-owned).
    /// </summary>
    public static class SteamInviteButton
    {
        public const string Label = "Inviter des amis";
        public const string OverlayDisabledMessage =
            "L'overlay Steam est désactivé : invite tes amis depuis ta liste d'amis Steam.";
        private const string TemplateName = "StartGame";
        private const float GapBelowCode = 8f;

        public static void AddUnder(RectTransform codeLabel)
        {
            if (!GameFlavor.IsSteam || codeLabel == null || codeLabel.parent == null)
            {
                return;
            }

            Button _template = null;
            foreach (Button _button in codeLabel.parent.GetComponentsInChildren<Button>(true))
            {
                if (_button.name == TemplateName)
                {
                    _template = _button;
                    break;
                }
            }
            if (_template == null)
            {
                Debug.LogWarning("[STEAM] No StartGame button next to the game code: no invite button.");
                return;
            }

            GameObject _copy = Object.Instantiate(_template.gameObject, codeLabel.parent);
            _copy.name = "SteamInviteButton";
            // Keep the look only: the template's own behaviour (start the game, host-only) must not come along.
            foreach (MonoBehaviour _behaviour in _copy.GetComponents<MonoBehaviour>())
            {
                if (!(_behaviour is Graphic) && !(_behaviour is Button))
                {
                    Object.DestroyImmediate(_behaviour);
                }
            }
            Button _invite = _copy.GetComponent<Button>();
            _invite.onClick = new Button.ButtonClickedEvent(); // drops the template's persistent and runtime listeners
            _invite.onClick.AddListener(OpenInviteOverlay);
            _invite.interactable = true;
            _copy.SetActive(true);

            foreach (TMP_Text _text in _copy.GetComponentsInChildren<TMP_Text>(true))
            {
                _text.text = Label;
            }

            var _rect = (RectTransform)_copy.transform;
            _rect.anchorMin = codeLabel.anchorMin;
            _rect.anchorMax = codeLabel.anchorMax;
            _rect.pivot = new Vector2(0.5f, 1f);
            float _codeBottom = codeLabel.anchoredPosition.y - codeLabel.pivot.y * codeLabel.rect.height;
            _rect.anchoredPosition = new Vector2(codeLabel.anchoredPosition.x, _codeBottom - GapBelowCode);
        }

        private static void OpenInviteOverlay()
        {
            LobbyManager _lobbyManager = LobbyManager.instance;
            GameLobby _lobby = _lobbyManager != null ? _lobbyManager.CurrentLobby : null;
            if (_lobby == null || !ulong.TryParse(_lobby.Id, out ulong _lobbyId) || !SteamClient.IsValid)
            {
                Debug.LogWarning("[STEAM] Invite clicked outside a Steam lobby.");
                GameLog.Info("ui", "ui.steam.invite.refused", "reason",
                    _lobby == null ? "no lobby" : !SteamClient.IsValid ? "steam not running" : "not a steam lobby");
                return;
            }
            if (!SteamUtils.IsOverlayEnabled)
            {
                GameLog.Info("ui", "ui.steam.invite.refused", "reason", "overlay disabled", "lobby", _lobby.Id);
                _lobbyManager.ReportError(OverlayDisabledMessage);
                return;
            }
            GameLog.Info("ui", "ui.steam.invite", "lobby", _lobby.Id);
            SteamFriends.OpenGameInviteOverlay(_lobbyId);
        }
    }
}
