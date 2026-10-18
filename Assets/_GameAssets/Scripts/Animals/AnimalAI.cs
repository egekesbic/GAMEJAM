using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(HoldableItem), typeof(NavMeshAgent), typeof(NetworkTransform))]
[DisallowMultipleComponent]
public sealed class AnimalAI : NetworkBehaviour, IHoldableItemPhysicsMode
{
    [Header("Movement")]
    [SerializeField, Min(0.1f)] private float _walkSpeed = 1.5f;
    [SerializeField, Min(0f)] private float _wanderRadius = 5f;
    [SerializeField, Min(0.1f)] private float _playerDetectionDistance = 5f;
    [SerializeField, Min(0.1f)] private float _fleeDistance = 7f;
    [SerializeField, Min(0.1f)] private float _fleeSpeedMultiplier = 1.5f;
    [SerializeField, Min(0.05f)] private float _decisionInterval = 0.3f;
    [SerializeField, Min(0.05f)] private float _fleeRepathInterval = 0.5f;
    [SerializeField, Min(0f)] private float _minimumIdleTime = 0.5f;
    [SerializeField, Min(0f)] private float _maximumIdleTime = 2f;
    [SerializeField, Min(0.1f)] private float _navMeshSampleRadius = 2f;
    [SerializeField] private string _deliveryItemId = "Animal";

    [Header("Scale Animation")]
    [Tooltip("Assign the animal model child here. The root stays untouched for physics and networking.")]
    [SerializeField] private Transform _visualRoot;
    [SerializeField, Range(0f, 0.4f)] private float _yScaleAmount = 0.12f;
    [SerializeField, Min(0.1f)] private float _walkCycleSpeed = 3.5f;

    private readonly NetworkVariable<bool> _isWalking = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> _isDelivered = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private HoldableItem _holdableItem;
    private Rigidbody _rigidbody;
    private NavMeshAgent _agent;
    private NetworkTransform _networkTransform;
    private NavMeshPath _candidatePath;
    private Vector3 _wanderCenter;
    private Vector3 _visualBaseScale;
    private float _animationTime;
    private float _nextDecisionTime;
    private float _nextFleeRepathTime;
    private float _nextNavMeshRetryTime;
    private float _idleUntil;
    private bool _waitingAtDestination;
    private bool _warnedNoNavMesh;

    public bool KeepRigidbodyKinematicWhenDropped => !_isDelivered.Value;
    public string DeliveryItemId => string.IsNullOrWhiteSpace(_deliveryItemId) ? "Animal" : _deliveryItemId.Trim();

    private void Awake()
    {
        _holdableItem = GetComponent<HoldableItem>();
        _rigidbody = GetComponent<Rigidbody>();
        _agent = GetComponent<NavMeshAgent>();
        _networkTransform = GetComponent<NetworkTransform>();
        _candidatePath = new NavMeshPath();

        _agent.speed = _walkSpeed;
        _agent.updatePosition = true;
        _agent.updateRotation = true;

        // The server drives the root transform; all clients receive its position and yaw.
        _networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
        _networkTransform.SyncPositionX = true;
        _networkTransform.SyncPositionY = true;
        _networkTransform.SyncPositionZ = true;
        _networkTransform.SyncRotAngleY = true;

        if (_visualRoot == null)
            _visualRoot = FindFirstVisualChild();

        if (_visualRoot != null)
            _visualBaseScale = _visualRoot.localScale;
        else
            Debug.LogWarning($"{name}: AnimalAI için bir görsel child atayın; ölçek animasyonu yapılmayacak.", this);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _wanderCenter = transform.position;
        if (!IsServer)
        {
            _agent.enabled = false;
            return;
        }

        if (!_holdableItem.IsHeld)
            EnableAgentOnNavMesh();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
            _isWalking.Value = false;

        if (_agent != null && _agent.enabled)
            _agent.enabled = false;

        base.OnNetworkDespawn();
    }

    private void Update()
    {
        UpdateScaleAnimation();

        if (!IsServer || !IsSpawned)
            return;

        if (_isDelivered.Value)
        {
            SetWalking(false);
            DisableAgent();
            return;
        }

        if (_holdableItem.IsHeld)
        {
            SetWalking(false);
            DisableAgent();
            return;
        }

        if (!_agent.enabled || !_agent.isOnNavMesh)
        {
            if (!EnableAgentOnNavMesh())
            {
                SetWalking(false);
                return;
            }
        }

        if (Time.time >= _nextDecisionTime)
        {
            _nextDecisionTime = Time.time + _decisionInterval;
            if (TryGetNearbyPlayer(out Vector3 playerPosition))
                FleeFrom(playerPosition);
            else
                WanderOrWait();
        }

        SetWalking(_agent.enabled && _agent.isOnNavMesh && _agent.velocity.sqrMagnitude > 0.01f);
    }

    public void OnHoldableStateChanged(bool isHeld)
    {
        if (isHeld)
        {
            _waitingAtDestination = false;
            if (IsServer && IsSpawned)
                SetWalking(false);
            DisableAgent();
            return;
        }

        if (!IsServer || _isDelivered.Value)
        {
            DisableAgent();
            return;
        }

        if (IsSpawned)
        {
            _wanderCenter = transform.position;
            _waitingAtDestination = false;
            _nextDecisionTime = Time.time + Random.Range(_minimumIdleTime, _maximumIdleTime);
            EnableAgentOnNavMesh();
        }
    }

    /// <summary>Stops the server-driven animal AI permanently after a successful delivery.</summary>
    public void StopAIForDelivery()
    {
        if (!IsServer || !IsSpawned || _isDelivered.Value)
            return;

        _isDelivered.Value = true;
        _waitingAtDestination = false;
        SetWalking(false);
        DisableAgent();

        // The animal is normally kept kinematic so the NavMeshAgent owns movement.
        // Once delivered, hand movement to server physics so it can fall onto the floor.
        if (_rigidbody != null)
        {
            _rigidbody.isKinematic = false;
            _rigidbody.useGravity = true;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.WakeUp();
        }
    }

    private void UpdateScaleAnimation()
    {
        if (_visualRoot == null)
            return;

        if (!IsSpawned || _holdableItem.IsHeld || !_isWalking.Value)
        {
            _visualRoot.localScale = _visualBaseScale;
            _animationTime = 0f;
            return;
        }

        _animationTime += Time.deltaTime * _walkCycleSpeed * Mathf.PI * 2f;
        float yScale = 1f + Mathf.Sin(_animationTime) * _yScaleAmount;
        _visualRoot.localScale = new Vector3(
            _visualBaseScale.x,
            _visualBaseScale.y * yScale,
            _visualBaseScale.z);
    }

    private bool TryGetNearbyPlayer(out Vector3 playerPosition)
    {
        playerPosition = default;
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || manager.SpawnManager == null)
            return false;

        float closestDistanceSqr = _playerDetectionDistance * _playerDetectionDistance;
        bool foundPlayer = false;
        foreach (NetworkObject networkObject in manager.SpawnManager.SpawnedObjectsList)
        {
            if (networkObject == null)
                continue;

            PlayerMovement player = networkObject.GetComponent<PlayerMovement>();
            if (player == null || !player.IsSpawned)
                continue;

            Vector3 offset = player.transform.position - transform.position;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr >= closestDistanceSqr)
                continue;

            closestDistanceSqr = distanceSqr;
            playerPosition = player.transform.position;
            foundPlayer = true;
        }

        return foundPlayer;
    }

    private void FleeFrom(Vector3 playerPosition)
    {
        _waitingAtDestination = false;
        if (Time.time < _nextFleeRepathTime && _agent.hasPath)
            return;

        _nextFleeRepathTime = Time.time + _fleeRepathInterval;
        _agent.speed = _walkSpeed * _fleeSpeedMultiplier;

        Vector3 away = transform.position - playerPosition;
        away.y = 0f;
        if (away.sqrMagnitude < 0.001f)
        {
            Vector2 randomDirection = Random.insideUnitCircle.normalized;
            away = new Vector3(randomDirection.x, 0f, randomDirection.y);
        }
        away.Normalize();

        for (int attempt = 0; attempt < 8; attempt++)
        {
            float angle = Random.Range(-65f, 65f);
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * away;
            Vector3 destination = transform.position + direction * _fleeDistance;
            if (TrySetDestination(destination, _navMeshSampleRadius * 2f))
                return;
        }
    }

    private void WanderOrWait()
    {
        _agent.speed = _walkSpeed;
        if (_agent.pathPending)
            return;

        if (_agent.hasPath && _agent.remainingDistance > _agent.stoppingDistance + 0.15f)
        {
            _waitingAtDestination = false;
            return;
        }

        if (!_waitingAtDestination)
        {
            _waitingAtDestination = true;
            _idleUntil = Time.time + Random.Range(_minimumIdleTime, _maximumIdleTime);
            _agent.ResetPath();
            return;
        }

        if (Time.time >= _idleUntil)
        {
            _waitingAtDestination = false;
            ChooseWanderDestination();
        }
    }

    private void ChooseWanderDestination()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * _wanderRadius;
            Vector3 destination = _wanderCenter + new Vector3(offset.x, 0f, offset.y);
            if (TrySetDestination(destination, _navMeshSampleRadius))
                return;
        }
    }

    private bool TrySetDestination(Vector3 destination, float sampleRadius)
    {
        if (!NavMesh.SamplePosition(destination, out NavMeshHit hit, sampleRadius, _agent.areaMask))
            return false;

        if (!_agent.CalculatePath(hit.position, _candidatePath) || _candidatePath.status != NavMeshPathStatus.PathComplete)
            return false;

        _agent.isStopped = false;
        return _agent.SetPath(_candidatePath);
    }

    private bool EnableAgentOnNavMesh()
    {
        if (!IsServer || _holdableItem.IsHeld || _isDelivered.Value)
            return false;

        if (_agent.enabled && _agent.isOnNavMesh)
            return true;

        if (_agent.enabled)
            _agent.enabled = false;

        if (Time.time < _nextNavMeshRetryTime)
            return false;

        _nextNavMeshRetryTime = Time.time + 1f;
        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, _navMeshSampleRadius, _agent.areaMask))
        {
            if (!_warnedNoNavMesh)
            {
                Debug.LogWarning($"{name}: Yakınında NavMesh yok. AI hareketi için sahnede NavMesh bake edin.", this);
                _warnedNoNavMesh = true;
            }
            return false;
        }

        _warnedNoNavMesh = false;
        transform.position = hit.position;
        _agent.enabled = true;
        if (!_agent.isOnNavMesh && !_agent.Warp(hit.position))
        {
            _agent.enabled = false;
            return false;
        }

        _wanderCenter = hit.position;
        _agent.speed = _walkSpeed;
        _agent.isStopped = false;
        return true;
    }

    private void DisableAgent()
    {
        if (_agent == null || !_agent.enabled)
            return;

        if (_agent.isOnNavMesh)
            _agent.ResetPath();
        _agent.enabled = false;
    }

    private void SetWalking(bool isWalking)
    {
        if (IsServer && IsSpawned && _isWalking.Value != isWalking)
            _isWalking.Value = isWalking;
    }

    private Transform FindFirstVisualChild()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return null;

        Transform candidate = renderers[0].transform;
        while (candidate != null && candidate != transform)
        {
            bool containsAllRenderers = true;
            foreach (Renderer renderer in renderers)
            {
                if (renderer.transform != candidate && !renderer.transform.IsChildOf(candidate))
                {
                    containsAllRenderers = false;
                    break;
                }
            }

            if (containsAllRenderers)
                return candidate;

            candidate = candidate.parent;
        }

        return renderers[0].transform != transform ? renderers[0].transform : null;
    }
}
