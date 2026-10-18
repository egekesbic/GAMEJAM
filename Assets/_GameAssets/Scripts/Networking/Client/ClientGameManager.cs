using Cysharp.Threading.Tasks;
using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ClientGameManager
{
    private JoinAllocation _joinAllocation;

    public async UniTask<bool> InitAsync()
    {
        await UnityServices.InitializeAsync();

        AuthenticationState authenticationState = await AuthenticationHandler.DoAuth();

        if (authenticationState == AuthenticationState.Authenticated)
        {
            return true;
        }

        return false;
    }

    public void GoToMainMenu()
    {
        SceneManager.LoadScene("MenuScene");
    }

    public async UniTask StartClientAsync(string joinCode)
    {
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            Debug.LogError("A Relay join code is required.");
            return;
        }

        try
        {
            _joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
        }
        catch(Exception ex)
        {
            Debug.LogError(ex);
            return;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            Debug.LogError("Cannot join Relay because NetworkManager is missing.");
            return;
        }

        UnityTransport transport = networkManager.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("Cannot join Relay because UnityTransport is missing.");
            return;
        }

        transport.SetRelayServerData(AllocationUtils.ToRelayServerData(_joinAllocation, "dtls"));
        if (!networkManager.StartClient())
            Debug.LogError("NetworkManager failed to start the client.");
    }
}
