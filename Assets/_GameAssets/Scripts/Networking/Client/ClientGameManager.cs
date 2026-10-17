using Cysharp.Threading.Tasks;
using System;
using Unity.AI.Navigation.LowLevel;
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
        try
        {
            _joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
        }
        catch(Exception ex)
        {
            Debug.LogError(ex);
        }

        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetRelayServerData(AllocationUtils.ToRelayServerData(_joinAllocation, "dtls"));
        NetworkManager.Singleton.StartClient();
    }
}