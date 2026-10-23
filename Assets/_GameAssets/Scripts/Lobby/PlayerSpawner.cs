
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
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
        {
            StartCoroutine(LogLocalClientSpawnCheckpoints());
            return;
        }

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

        Debug.Log(
            $"[SpawnTrace][Server][{Time.time:F2}s] Starting spawn. Client={clientId}, " +
            $"point={spawnPoint.name}[{selectedIndex}], pointPosition={spawnPoint.position}, " +
            $"pointRotation={spawnPoint.rotation.eulerAngles}, existingPlayer=" +
            (existingPlayer != null ? $"{existingPlayer.name} spawned={existingPlayer.IsSpawned} position={existingPlayer.transform.position}" : "none"),
            this);

        // Replace an untracked player object so it cannot keep an arbitrary old/default position.
        if (existingPlayer != null && existingPlayer.IsSpawned)
            existingPlayer.Despawn(true);

        NetworkObject player = Instantiate(
            _playerPrefab,
            spawnPoint.position,
            spawnPoint.rotation
        );

        player.SpawnAsPlayerObject(clientId);

        LogServerSpawnState("immediately after SpawnAsPlayerObject", player, spawnPoint.position);

        // The player prefab uses an owner-authoritative NetworkTransform. Reapply the
        // server-selected spawn transform after ownership is assigned so the new owner
        // cannot initialize at the prefab's default position (commonly world origin).
        if (player.TryGetComponent(out NetworkTransform playerTransform))
            playerTransform.SetState(spawnPoint.position, spawnPoint.rotation, teleportDisabled: false);

        LogServerSpawnState("after immediate NetworkTransform.SetState", player, spawnPoint.position);

        // Reassert the spawn after NetworkTransform initialization and physics'
        // first fixed step, when the new owner may publish its initial state.
        StartCoroutine(ReapplySpawnAfterInitialization(player, spawnPoint.position, spawnPoint.rotation));

        _occupiedSpawnPoints.Add(selectedIndex);
        _spawnPointByClient[clientId] = selectedIndex;

        Debug.Log(
            $"Client {clientId}, '{spawnPoint.name}' (index {selectedIndex}) noktasında doğdu. " +
            $"Spawn position: {spawnPoint.position}; player position: {player.transform.position}",
            player);
    }

    private IEnumerator ReapplySpawnAfterInitialization(
        NetworkObject player,
        Vector3 spawnPosition,
        Quaternion spawnRotation)
    {
        yield return new WaitForFixedUpdate();

        if (player == null || !player.IsSpawned || !IsServer)
            yield break;

        if (player.TryGetComponent(out Rigidbody playerRigidbody))
        {
            playerRigidbody.position = spawnPosition;
            playerRigidbody.rotation = spawnRotation;
            playerRigidbody.linearVelocity = Vector3.zero;
            playerRigidbody.angularVelocity = Vector3.zero;
        }
        else
        {
            player.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        }

        if (player.TryGetComponent(out NetworkTransform playerTransform))
            playerTransform.SetState(spawnPosition, spawnRotation, teleportDisabled: false);

        LogServerSpawnState("after first FixedUpdate reapply", player, spawnPosition);
    }

    private IEnumerator LogLocalClientSpawnCheckpoints()
    {
        float timeout = Time.realtimeSinceStartup + 10f;
        NetworkObject localPlayer = null;

        while (Time.realtimeSinceStartup < timeout)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening && manager.LocalClient != null)
                localPlayer = manager.LocalClient.PlayerObject;

            if (localPlayer != null && localPlayer.IsSpawned)
                break;

            yield return null;
        }

        if (localPlayer == null || !localPlayer.IsSpawned)
        {
            Debug.LogError(
                $"[SpawnTrace][Client][{Time.time:F2}s] Local PlayerObject was not assigned within 10 seconds. " +
                $"NetworkManager={NetworkManager.Singleton != null}, localClientId=" +
                (NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId.ToString() : "n/a"),
                this);
            yield break;
        }

        LogLocalPlayerState("first available", localPlayer);
        yield return new WaitForSeconds(1f);
        if (localPlayer != null && localPlayer.IsSpawned)
            LogLocalPlayerState("1 second after first available", localPlayer);

        yield return new WaitForSeconds(4f);
        if (localPlayer != null && localPlayer.IsSpawned)
            LogLocalPlayerState("5 seconds after first available", localPlayer);
    }

    private void LogServerSpawnState(string phase, NetworkObject player, Vector3 expectedPosition)
    {
        if (player == null)
            return;

        Rigidbody body = player.GetComponent<Rigidbody>();
        NetworkTransform networkTransform = player.GetComponent<NetworkTransform>();
        Debug.Log(
            $"[SpawnTrace][Server][{Time.time:F2}s] {phase}. Client={player.OwnerClientId}, " +
            $"NetworkObjectId={player.NetworkObjectId}, expected={expectedPosition}, " +
            $"transform={player.transform.position}, rigidbody={(body != null ? body.position.ToString() : "none")}, " +
            $"owner={player.IsOwner}, server={NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer}, spawned={player.IsSpawned}, " +
            $"NetworkTransform={(networkTransform != null ? networkTransform.AuthorityMode.ToString() : "missing")}",
            player);
    }

    private void LogLocalPlayerState(string phase, NetworkObject player)
    {
        Rigidbody body = player.GetComponent<Rigidbody>();
        NetworkTransform networkTransform = player.GetComponent<NetworkTransform>();
        Debug.Log(
            $"[SpawnTrace][Client][{Time.time:F2}s] {phase}. LocalClient={NetworkManager.Singleton.LocalClientId}, " +
            $"PlayerOwner={player.OwnerClientId}, NetworkObjectId={player.NetworkObjectId}, " +
            $"transform={player.transform.position}, rigidbody={(body != null ? body.position.ToString() : "none")}, " +
            $"owner={player.IsOwner}, server={NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer}, spawned={player.IsSpawned}, " +
            $"NetworkTransform={(networkTransform != null ? networkTransform.AuthorityMode.ToString() : "missing")}",
            player);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!_spawnPointByClient.TryGetValue(clientId, out int spawnPointIndex))
            return;

        _spawnPointByClient.Remove(clientId);
        _occupiedSpawnPoints.Remove(spawnPointIndex);
    }

}
