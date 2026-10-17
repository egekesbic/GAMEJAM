
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
        _readyButton.onClick.AddListener(ReadyButtonOnClick);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            string roomCode = HostGameManager.Instance.GetRoomCode();
            SetRoomCodeClientRpc(roomCode);
        }
    }

    [ClientRpc]
    private void SetRoomCodeClientRpc(string roomCode)
    {
        _roomCodeText.text = "Room Code: " + roomCode;
    }

    private void ReadyButtonOnClick()
    {
        LobbyGameManager.Instance.Ready_ServerRpc(IsServer);
    }

    private void OnDestroy()
    {
        _readyButton.onClick.RemoveListener(ReadyButtonOnClick);
    }
}
