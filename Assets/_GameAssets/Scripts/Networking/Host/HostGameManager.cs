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
    private const int MAX_CONNECTIONS = 4;

    private Allocation _allocation;

    private string _joinCode;
    public async UniTask StartHostAsync()
    {
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

        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetRelayServerData(AllocationUtils.ToRelayServerData(_allocation, "dtls"));
        NetworkManager.Singleton.StartHost();

        NetworkManager.Singleton.SceneManager.LoadScene("LobbyScene",LoadSceneMode.Single);
    }

}
