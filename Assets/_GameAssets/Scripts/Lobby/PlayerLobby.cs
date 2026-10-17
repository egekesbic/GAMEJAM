using Unity.Netcode;
using UnityEngine;

public class PlayerLobby : NetworkBehaviour
{
    private bool send;

    private void Update()
    {
        if (!IsSpawned || !IsClient || IsServer || send)
            return;

        if (LobbyGameManager.Instance == null)
            return;

        LobbyGameManager.Instance.ClientJoin_ServerRPC();
        send = true;
    }
}