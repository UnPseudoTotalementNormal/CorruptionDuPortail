using System;
using System.Threading.Tasks;
using DG.Tweening;
using Extensions;
using Network;
using Network.Player;
using Network.Steam;
using Steamworks;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class LoginMenu : MonoBehaviour
{
    [SerializeField] private CanvasGroup menuCanvasGroup;
    [SerializeField] private CanvasGroup loadingCanvasGroup;
    [SerializeField] private CanvasGroup loginCanvasGroup;
    [SerializeField] private float fadeDuration = 0.25f;
    
    [SerializeField] private TMP_InputField usernameInputField;
    
    [SerializeField] private TMP_Text errorText;

    [SerializeField] private Button loginButton;

    private void Awake()
    {
        if (GameFlavor.IsSteam)
        {
            EnterWithSteamProfile();
            return;
        }

        if (UnityServices.Instance.State == ServicesInitializationState.Initialized)
        {
            menuCanvasGroup.DoHideGroup(0);
            return;
        }
        
        loginCanvasGroup.DoHideGroup(0);
        loadingCanvasGroup.DoShowGroup();
        _ = Init();
        loginButton.onClick.AddListener(() => _ = OnSignInButtonClicked());

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            string _profile = "Player_" + System.Guid.NewGuid().ToString("N").Substring(0, 15);
            AuthenticationService.Instance.SwitchProfile(_profile);
        }
    }

    // Steam build: the Steam profile IS the login. No UGS account (Steam lobbies, Steam relay), no form: the player
    // lands on the main menu under their Steam name. With Steam closed nothing online works, so the login screen stays
    // as a blocking notice whose button retries.
    private void EnterWithSteamProfile()
    {
        if (SteamRuntime.TryStartClient())
        {
            LocalPlayerInfoHolder.CreateSteamClientData(SteamClient.Name, SteamClient.SteamId.Value);
            menuCanvasGroup.DoHideGroup(0);
            return;
        }

        loadingCanvasGroup.DoHideGroup(0);
        loginCanvasGroup.DoShowGroup(0);
        usernameInputField.gameObject.SetActive(false);
        TMP_Text _buttonLabel = loginButton.GetComponentInChildren<TMP_Text>(true);
        if (_buttonLabel != null)
        {
            _buttonLabel.text = "Réessayer";
        }
        loginButton.onClick.RemoveAllListeners();
        loginButton.onClick.AddListener(EnterWithSteamProfile);
        errorText.DOKill(true);
        errorText.alpha = 1;
        errorText.text = SteamRuntime.NotRunningMessage;
    }

    private async Task Init()
    {
        ShowLoading();
        await UnityServices.InitializeAsync();
        ShowLogin();
    }

    public async Task OnSignInButtonClicked()
    {
        ShowLoading();
        var _result = await SignIn(usernameInputField.text.Trim());
        
        if (_result)
        {
            ShowMainMenu();
        }
        else
        {
            if (AuthenticationService.Instance.IsSignedIn)
            {
                AuthenticationService.Instance.SignOut();
            }
            ShowLogin();
        }
    }

    private async Task<bool> SignIn(string username)
    {
        try
        {
            if (AuthenticationService.Instance.IsSignedIn)
            {
                DisplayError("Already signed in. (Somehow ???)");
                return false;
            }

            Debug.Log("Switching profile to: " + username);
            
            string sanitizedUsername = SanitizeUsername(username);
            Debug.Log("Sanitized username: " + sanitizedUsername);
            
            AuthenticationService.Instance.SwitchProfile(sanitizedUsername);
            
            Debug.Log("Signing in anonymously...");
            await SignInAnonymously();
            
            Debug.Log("Signed in. Player ID: " + AuthenticationService.Instance.PlayerId);

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                DisplayError("Sign-in failed for an unknown reason.");
                return false;
            }
            
            Debug.Log("Setting username: " + username);

            try
            {
                string nameResult = await AuthenticationService.Instance.UpdatePlayerNameAsync(username);
                Debug.Log("Username set to: " + nameResult);
                // NET-02: UGS names carry the "#1234" discriminator, stripped for display.
                LocalPlayerInfoHolder.CreateNewClientData(nameResult, true);
            }
            catch (Exception e)
            {
                DisplayError($"Set username failed: {e.Message}");
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            DisplayError($"Sign-in failed: {e.Message}");
            
            return false;
        }
    }
    
    private async Task SignInAnonymously()
    {
        await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }
    
    private string SanitizeUsername(string username)
    {
        if (string.IsNullOrEmpty(username))
        {
            return "User" + UnityEngine.Random.Range(1000, 9999);
        }
        
        // Remove spaces and keep only alphanumeric characters
        string sanitized = "";
        foreach (char c in username)
        {
            if (char.IsLetterOrDigit(c))
            {
                sanitized += c;
            }
        }
        
        // If empty after sanitization, use a default name
        if (string.IsNullOrEmpty(sanitized))
        {
            sanitized = "User" + UnityEngine.Random.Range(1000, 9999);
        }
        
        // Limit to 30 characters
        if (sanitized.Length > 30)
        {
            sanitized = sanitized.Substring(0, 30);
        }
        
        return sanitized;
    }
    
    private void DisplayError(string message)
    {
        errorText.text = message;
        errorText.DOKill(true);
        errorText.alpha = 1;
        errorText.DOFade(0, 2f).SetDelay(3f);
    }

    private void ShowLogin()
    {
        Debug.Log("Showing login menu");
        loadingCanvasGroup.DoHideGroup(fadeDuration);
        loginCanvasGroup.DoShowGroup(fadeDuration);
    }
    
    private void ShowLoading()
    {
        Debug.Log("Showing loading menu");
        loginCanvasGroup.DoHideGroup(fadeDuration);
        loadingCanvasGroup.DoShowGroup(fadeDuration);
    }
    
    private void ShowMainMenu()
    {
        menuCanvasGroup.DoHideGroup(fadeDuration);
    }
}
