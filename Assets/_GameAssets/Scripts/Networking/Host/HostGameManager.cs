using Cysharp.Threading.Tasks;
using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

public class HostGameManager
{
    // The lobby has one host slot and one client slot.
    private const int MAX_CONNECTIONS = 1;

    private Allocation _allocation;

    private string _joinCode;

    public async UniTask StartHostAsync()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            Debug.LogError("Cannot start the host because NetworkManager is missing.");
            return;
        }

        UnityTransport transport = networkManager.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("Cannot start the host because UnityTransport is missing.");
            return;
        }

        try
        {
           _allocation = await RelayService.Instance.CreateAllocationAsync(MAX_CONNECTIONS);
        }
        catch(Exception exception)
        {
            Debug.LogError(exception);
            return;
        }

        try
        {
            _joinCode = await RelayService.Instance.GetJoinCodeAsync(_allocation.AllocationId);
            Debug.Log(_joinCode);
        }
        catch(Exception exception)
        {
            Debug.LogError(exception);
            return;
        }  

        transport.SetRelayServerData(AllocationUtils.ToRelayServerData(_allocation, "dtls"));
        if (!networkManager.StartHost())
        {
            Debug.LogError("NetworkManager failed to start the host.");
            return;
        }

        if (networkManager.SceneManager == null)
        {
            Debug.LogError("Cannot load the lobby because Netcode scene management is disabled.");
            return;
        }

        networkManager.SceneManager.LoadScene("LobbyScene", LoadSceneMode.Single);
    }

    public string GetRoomCode()
    {
        return _joinCode;
    }

}
