#region

using System.Threading.Tasks;
using Network;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#endregion

public class NetworkHUD : MonoBehaviour
{
    [SerializeField] private GameObject _panelObject;
    
    [SerializeField] private Button _hostButton;
    [SerializeField] private Button _joinButton;
    
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private TextMeshProUGUI hostCodeText;

    public async void OnJoinButtonClicked()
    {
        bool _isConnected = await StartClientWithRelay(inputField.text, "udp");

        if (!_isConnected)
        {
            return;
        }
        _hostButton.interactable = false;
        _joinButton.interactable = false;

        GameCode.gameCode = inputField.text;
        
        GUIUtility.systemCopyBuffer = GameCode.gameCode;
        
        SwitchToGameScene();
    }

    public async void OnHostButtonClicked()
    {
        string joinCode = await StartHostWithRelay(15, "udp");
        _hostButton.interactable = false;
        _joinButton.interactable = false;
        inputField.gameObject.SetActive(false);
        hostCodeText.gameObject.SetActive(true);
        hostCodeText.text = joinCode;
        
        GameCode.gameCode = joinCode;
        
        GUIUtility.systemCopyBuffer = joinCode;

        SwitchToGameScene();
    }
    
    public async Task<string> StartHostWithRelay(int maxConnections, string connectionType)
    {
        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        var allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, connectionType));
        var joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        return NetworkManager.Singleton.StartHost() ? joinCode : null;
    }
    
    public async Task<bool> StartClientWithRelay(string joinCode, string connectionType)
    {
        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        var allocation = await RelayService.Instance.JoinAllocationAsync(joinCode: joinCode);
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, connectionType));
        return !string.IsNullOrEmpty(joinCode) && NetworkManager.Singleton.StartClient();
    }

    private void SwitchToGameScene()
    {
        NetworkManager.Singleton.SceneManager.LoadScene(
            "GameScene",
            LoadSceneMode.Single);
    }

    public void HideButtonClicked()
    {
        _panelObject.SetActive(!_panelObject.activeSelf);
    }
}
