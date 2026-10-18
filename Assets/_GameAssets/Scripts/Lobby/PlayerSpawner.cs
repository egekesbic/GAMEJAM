
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

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) &&
            client.PlayerObject != null)
        {
            ReserveNearestSpawnPoint(clientId, client.PlayerObject.transform.position);
            return;
        }

        if (_playerPrefab == null || _spawnPoints == null ||
            _spawnPoints.Length == 0)
        {
            Debug.LogError("PlayerSpawner: Prefab veya spawn noktaları eksik!");
            return;
        }

        List<int> availablePoints = new();

        for (int i = 0; i < _spawnPoints.Length; i++)
        {
            if (_spawnPoints[i] != null &&
                !_occupiedSpawnPoints.Contains(i))
            {
                availablePoints.Add(i);
            }
        }

        if (availablePoints.Count == 0)
        {
            Debug.LogWarning("Boş spawn noktası yok!");
            return;
        }

        int selectedIndex = availablePoints[
            Random.Range(0, availablePoints.Count)];

        _occupiedSpawnPoints.Add(selectedIndex);

        Transform spawnPoint = _spawnPoints[selectedIndex];

        NetworkObject player = Instantiate(
            _playerPrefab,
            spawnPoint.position,
            spawnPoint.rotation
        );

        player.SpawnAsPlayerObject(clientId);
        _spawnPointByClient[clientId] = selectedIndex;

        Debug.Log($"Client {clientId}, Spawn Point {selectedIndex} noktasında doğdu.");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!_spawnPointByClient.TryGetValue(clientId, out int spawnPointIndex))
            return;

        _spawnPointByClient.Remove(clientId);
        _occupiedSpawnPoints.Remove(spawnPointIndex);
    }

    private void ReserveNearestSpawnPoint(ulong clientId, Vector3 playerPosition)
    {
        if (_spawnPointByClient.ContainsKey(clientId) || _spawnPoints == null)
            return;

        int nearestIndex = -1;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < _spawnPoints.Length; i++)
        {
            if (_spawnPoints[i] == null || _occupiedSpawnPoints.Contains(i))
                continue;

            float distance = (playerPosition - _spawnPoints[i].position).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }

        if (nearestIndex < 0)
            return;

        _occupiedSpawnPoints.Add(nearestIndex);
        _spawnPointByClient[clientId] = nearestIndex;
    }
}
