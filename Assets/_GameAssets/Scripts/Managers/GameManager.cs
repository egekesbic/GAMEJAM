
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
    [SerializeField] private PlayerChacterType _player2Type;

    private bool _hostIsMale;

    private int _selectedIndex;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (!IsServer)
        {
            return;
        }

        SetHostChacter();
    }

    private void SetHostChacter()
    {
        _player1Type = (PlayerChacterType)Random.Range(0, 2);
        _selectedIndex = (int)_player1Type;

        _player1.SetActive(true);

        bool isMale = _player1Type == PlayerChacterType.Male;

        _player1maleCharacter.gameObject.SetActive(isMale);
        _player1FemaleCharacter.gameObject.SetActive(!isMale);
    }


    [ServerRpc]
    public void ClientJoin_ServerRPC()
    {
        AsekronHost_RPC(_hostIsMale);
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
}
