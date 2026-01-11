using System;
using System.Threading.Tasks;
using DG.Tweening;
using Extensions;
using Netcode.Transports.Facepunch;
using Network.Player;
using Network.Services;
using Steamworks;
using TMPro;
using Unity.Netcode;
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

    private bool _useSteamAuth = false;
    
    private void Awake()
    {
        if (UnityServices.Instance.State == ServicesInitializationState.Initialized)
        {
            menuCanvasGroup.DoHideGroup(0);
            return;
        }
        
        loginCanvasGroup.DoHideGroup(0);
        loadingCanvasGroup.DoShowGroup();
        _ = Init();
        loginButton.onClick.AddListener(() => _ = OnSignInButtonClicked());
        
        //if is using punchface steam transport, login with steam
        var networkManager = NetworkManager.Singleton;
        if (networkManager != null &&
            networkManager.NetworkConfig.NetworkTransport is FacepunchTransport)
        {
            _useSteamAuth = true;
            string steamUsername = SteamClient.Name;
            
            usernameInputField.text = steamUsername;
            _ = OnSignInButtonClicked();
        }
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
        var _result = await SignIn(usernameInputField.text.Trim(), _useSteamAuth);
        
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

    private async Task<bool> SignIn(string username, bool useSteamAuth = false)
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
            
            Debug.Log("Starting sign-in process...");
            // Sign in based on authentication type
            if (useSteamAuth)
            {
                Debug.Log("Signing in with Steam...");
                await SignInWithSteam();
            }
            else
            {
                Debug.Log("Signing in anonymously...");
                await SignInAnonymously();
            }
            
            Debug.Log("Signed in. Player ID: " + AuthenticationService.Instance.PlayerId);

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                DisplayError("Sign-in failed for an unknown reason.");
                return false;
            }
            
            Debug.Log("Setting username: " + username);

            try
            {
                string nameResult;
                Debug.Log("Setting username with steamauth: " + _useSteamAuth);
                if (_useSteamAuth)
                {
                    nameResult = SteamClient.Name;
                }
                else
                {
                    nameResult = await AuthenticationService.Instance.UpdatePlayerNameAsync(username);
                }
                Debug.Log("Username set to: " + nameResult);
                LocalPlayerInfoHolder.CreateNewClientData(nameResult);
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
    
    private async Task SignInWithSteam()
    {
        string steamId = SteamClient.SteamId.ToString();
        string hashedSteamId = HashSteamId(steamId);


        Debug.Log(AuthenticationService.Instance.IsSignedIn);
        
        try
        {
            await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(hashedSteamId, "Ab.3" + hashedSteamId);
        }
        catch
        {
            await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(hashedSteamId, "Ab.3" + hashedSteamId);
        }

        return;
        
        // string authTicket = await GetSteamSessionTicket();
        //
        // var options = new SignInOptions { CreateAccount = true };
        //
        // await AuthenticationService.Instance.SignInWithSteamAsync(
        //     authTicket,
        //     steamId,
        //     GameValues.APPID.ToString(),
        //     options
        // );
    }

    private string HashSteamId(string steamId)
    {
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            byte[] hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(steamId));
            string hex = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            return hex.Substring(0, 20);
        }
    }


    
    private async Task<string> GetSteamSessionTicket()
    {
        var ticket = await SteamUser.GetAuthSessionTicketAsync();
        return BitConverter.ToString(ticket.Data).Replace("-", "");
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
