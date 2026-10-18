
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class LobbyGameManager : NetworkBehaviour
{
    public static LobbyGameManager Instance { get; private set; }

    [Header("Lobby'deki iki görsel oyuncu slotu")]
    [SerializeField] private GameObject _player1;
    [SerializeField] private PlayerChacterType _player1Type;

    [SerializeField] private SkinnedMeshRenderer _player1maleCharacter;
    [SerializeField] private SkinnedMeshRenderer _player1FemaleCharacter;

    [SerializeField] private GameObject _player2;
    [SerializeField] private SkinnedMeshRenderer _player2maleCharacter;
    [SerializeField] private SkinnedMeshRenderer _player2FemaleCharacter;

    [Header("Ready Text")]
    [SerializeField] private TMP_Text _player1Text;
    [SerializeField] private TMP_Text _player2Text;

    private bool _hostIsMale;
    private int _selectedIndex;

    private bool _player1Ready;
    private bool _player2Ready;
    private bool _isStartingGame;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            SetHostChacter();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        base.OnNetworkDespawn();
    }

    private void SetHostChacter()
    {
        _player1Type = (PlayerChacterType)Random.Range(0, 2);
        _selectedIndex = (int)_player1Type;

        _hostIsMale = _player1Type == PlayerChacterType.Male;

        _player1.SetActive(true);
        _player1maleCharacter.gameObject.SetActive(_hostIsMale);
        _player1FemaleCharacter.gameObject.SetActive(!_hostIsMale);

        AsekronHost_RPC(_hostIsMale);

        SetReadyText_RPC(true, "Not Ready");
        SetReadyText_RPC(false, "Not Ready");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ClientJoin_ServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer)
            return;

        AsekronHost_RPC(_hostIsMale);

        SetReadyText_RPC(
            true,
            _player1Ready ? "Ready" : "Not Ready"
        );

        SetReadyText_RPC(
            false,
            _player2Ready ? "Ready" : "Not Ready"
        );
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void AsekronHost_RPC(bool isMale)
    {
        _player1.SetActive(true);
        _player1maleCharacter.gameObject.SetActive(isMale);
        _player1FemaleCharacter.gameObject.SetActive(!isMale);

        _player2.SetActive(true);
        _player2maleCharacter.gameObject.SetActive(!isMale);
        _player2FemaleCharacter.gameObject.SetActive(isMale);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void Ready_ServerRpc(
        bool isServer,
        RpcParams rpcParams = default)
    {
        if (!IsServer || _isStartingGame)
            return;

        // Oyuncunun kimliğini istemcinin gönderdiği bool yerine
        // gerçek bağlantı kimliğinden belirle.
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        bool isHost = senderClientId == NetworkManager.ServerClientId;

        if (isServer != isHost)
        {
            Debug.LogWarning("Ready isteğindeki oyuncu bilgisi uyuşmuyor.");
            return;
        }

        if (isHost)
        {
            _player1Ready = !_player1Ready;

            SetReadyText_RPC(
                true,
                _player1Ready ? "Ready" : "Not Ready"
            );
        }
        else
        {
            _player2Ready = !_player2Ready;

            SetReadyText_RPC(
                false,
                _player2Ready ? "Ready" : "Not Ready"
            );
        }

        if (_player1Ready && _player2Ready)
        {
            StartGame();
        }
    }

    private void StartGame()
    {
        if (!IsServer || _isStartingGame)
            return;

        _isStartingGame = true;

        // Eski lobby PlayerObject'larını bulup despawn et.
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(
                    clientId, out var client))
                continue;

            NetworkObject oldPlayer = client.PlayerObject;

            if (oldPlayer != null && oldPlayer.IsSpawned)
            {
                oldPlayer.Despawn(true);
            }
        }

        // Lobby sahnesini kaldır ve GameScene'i yükle.
        NetworkManager.Singleton.SceneManager.LoadScene(
            "GameScene",
            UnityEngine.SceneManagement.LoadSceneMode.Single
        );
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SetReadyText_RPC(bool isPlayer1, string text)
    {
        if (isPlayer1)
        {
            _player1Text.text = text;
        }
        else
        {
            _player2Text.text = text;
        }
    }
}
