
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerSpawner : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkObject _playerPrefab;
    [SerializeField] private Transform[] _spawnPoints;

    private readonly HashSet<int> _occupiedSpawnPoints = new();
    private readonly Dictionary<ulong, int> _spawnPointByClient = new();

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            return;

        _occupiedSpawnPoints.Clear();
        _spawnPointByClient.Clear();
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            SpawnPlayer(clientId);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        SpawnPlayer(clientId);
    }

    private void SpawnPlayer(ulong clientId)
    {
        if (!IsServer)
            return;

        if (_spawnPointByClient.ContainsKey(clientId))
            return;

        NetworkObject existingPlayer = null;
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
            existingPlayer = client.PlayerObject;

        if (_playerPrefab == null || _spawnPoints == null ||
            _spawnPoints.Length == 0)
        {
            Debug.LogError("PlayerSpawner: Prefab veya spawn noktaları eksik!");
            return;
        }

        int selectedIndex = -1;
        for (int i = 0; i < _spawnPoints.Length; i++)
        {
            if (_spawnPoints[i] != null &&
                !_occupiedSpawnPoints.Contains(i))
            {
                selectedIndex = i;
                break;
            }
        }

        if (selectedIndex < 0)
        {
            Debug.LogWarning("Boş spawn noktası yok!");
            return;
        }

        Transform spawnPoint = _spawnPoints[selectedIndex];

        // Replace an untracked player object so it cannot keep an arbitrary old/default position.
        if (existingPlayer != null && existingPlayer.IsSpawned)
            existingPlayer.Despawn(true);

        NetworkObject player = Instantiate(
            _playerPrefab,
            spawnPoint.position,
            spawnPoint.rotation
        );

        player.SpawnAsPlayerObject(clientId);
        _occupiedSpawnPoints.Add(selectedIndex);
        _spawnPointByClient[clientId] = selectedIndex;

        Debug.Log($"Client {clientId}, '{spawnPoint.name}' (index {selectedIndex}) noktasında doğdu: {spawnPoint.position}");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!_spawnPointByClient.TryGetValue(clientId, out int spawnPointIndex))
            return;

        _spawnPointByClient.Remove(clientId);
        _occupiedSpawnPoints.Remove(spawnPointIndex);
    }

}
