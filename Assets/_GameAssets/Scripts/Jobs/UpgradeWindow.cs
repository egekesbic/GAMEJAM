using Unity.Netcode;
using UnityEngine;

public class UpgradeWindow : NetworkBehaviour, IPlayerInteractable
{
    [Header("Barricade Settings")]
    [SerializeField] private GameObject[] _barricadeBoards;
    [SerializeField] private float _repairDistance = 3f;
    [SerializeField] private string _repairPrompt = "F - Pencereyi tamir et";

    private readonly NetworkVariable<int> _repairedBoards = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public bool CanRepair => _barricadeBoards != null &&
                             _repairedBoards.Value < _barricadeBoards.Length;
    public string InteractionPrompt => CanRepair ? _repairPrompt : string.Empty;

    public void Interact(Vector3 rayOrigin, Vector3 rayDirection)
    {
        if (CanRepair)
            RepairServerRpc();
    }

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

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RepairServerRpc(RpcParams rpcParams = default)
    {
        if (!IsSpawned || !IsServer || !CanRepair)
            return;

        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) ||
            client.PlayerObject == null)
            return;

        float distance = Vector3.Distance(client.PlayerObject.transform.position, transform.position);
        if (distance > _repairDistance)
            return;

        _repairedBoards.Value++;
    }
}
