
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : NetworkBehaviour
{
    [SerializeField] private Button _readyButton;
    [SerializeField] private TMP_Text _readyText;
    [SerializeField] private TMP_Text _roomCodeText;

    private void Awake()
    {
        if (_readyButton != null)
            _readyButton.onClick.AddListener(ReadyButtonOnClick);
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            return;

        HostGameManager hostGameManager = HostSingleton.Instance != null
            ? HostSingleton.Instance.HostGameManager
            : null;
        if (hostGameManager == null)
        {
            Debug.LogWarning("LobbyUI cannot display a room code because the host manager is unavailable.", this);
            return;
        }

        SetRoomCodeClientRpc(hostGameManager.GetRoomCode());
    }

    [ClientRpc]
    private void SetRoomCodeClientRpc(string roomCode)
    {
        if (_roomCodeText != null)
            _roomCodeText.text = "Room Code: " + roomCode;
    }

    private void ReadyButtonOnClick()
    {
        if (LobbyGameManager.Instance != null && IsSpawned)
            LobbyGameManager.Instance.Ready_ServerRpc(IsServer);
    }

    public override void OnDestroy()
    {
        if (_readyButton != null)
            _readyButton.onClick.RemoveListener(ReadyButtonOnClick);

        base.OnDestroy();
    }
}
