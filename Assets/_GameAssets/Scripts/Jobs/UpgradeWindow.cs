
using Unity.Netcode;
using UnityEngine;

public class UpgradeWindow : NetworkBehaviour
{
    [Header("Barricade Settings")]
    [SerializeField] private GameObject[] _barricadeBoards;
    [SerializeField] private float _repairDistance = 3f;

    private readonly NetworkVariable<int> _repairedBoards =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    public bool CanRepair =>
        _barricadeBoards != null &&
        _repairedBoards.Value < _barricadeBoards.Length;

    public override void OnNetworkSpawn()
    {
        _repairedBoards.OnValueChanged += OnRepairedBoardsChanged;
        UpdateBarricade(_repairedBoards.Value);
    }

    public override void OnNetworkDespawn()
    {
        _repairedBoards.OnValueChanged -= OnRepairedBoardsChanged;
    }

    private void OnRepairedBoardsChanged(int previousValue, int newValue)
    {
        UpdateBarricade(newValue);
    }

    private void UpdateBarricade(int repairedCount)
    {
        if (_barricadeBoards == null)
            return;

        for (int i = 0; i < _barricadeBoards.Length; i++)
        {
            if (_barricadeBoards[i] != null)
                _barricadeBoards[i].SetActive(i < repairedCount);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void RepairServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsSpawned || !IsServer || !CanRepair)
            return;

        ulong clientId = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.ConnectedClients.TryGetValue(
                clientId, out NetworkClient client))
            return;

        if (client.PlayerObject == null)
            return;

        float distance = Vector3.Distance(
            client.PlayerObject.transform.position,
            transform.position
        );

        if (distance > _repairDistance)
            return;

        _repairedBoards.Value++;
    }
}
