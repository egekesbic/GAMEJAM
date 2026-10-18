using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(NetworkObject))]
public sealed class WaterValve : NetworkBehaviour
{
    [Header("Player UI")]
    [SerializeField] private GameObject _valveBackground;
    [SerializeField] private GameObject _valveSprite;

    [Header("Valve Interaction")]
    [SerializeField, Min(1)] private int _clicksToComplete = 10;
    [SerializeField, Min(0f)] private float _completionRotationDegrees = 180f;
    [SerializeField, Min(0.01f)] private float _rotationAnimationDuration = 0.2f;
    [SerializeField, Min(0.1f)] private float _idleResetDelay = 1f;
    [SerializeField, Min(0.1f)] private float _interactionDistance = 3f;
    [SerializeField] private UnityEvent _onTaskCompleted = new UnityEvent();

    private readonly NetworkVariable<ulong> _activeValveId = new NetworkVariable<ulong>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _progress = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> _completed = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private WaterValveInteractable _activeValve;
    private RectTransform _rotationTarget;
    private Quaternion _initialRotation;
    private Quaternion _targetRotation;
    private bool _interactionActive;
    private bool _hasTurnInput;
    private float _lastTurnInputTime;
    private bool _completionEventInvoked;

    public bool IsCompleted => _completed.Value;
    public int Progress => _progress.Value;
    public bool IsInteractionActive => _interactionActive && !_completed.Value;

    private void Awake()
    {
        CacheRotationTarget();
        SetBackgroundVisible(false);
    }

    public override void OnNetworkSpawn()
    {
        _activeValveId.OnValueChanged += OnActiveValveChanged;
        _progress.OnValueChanged += OnProgressChanged;
        _completed.OnValueChanged += OnCompletedChanged;

        if (_activeValveId.Value != 0)
            AttachToValve(_activeValveId.Value);

        ApplyProgress(_progress.Value);
        if (_completed.Value)
            CompleteTask();
    }

    public override void OnNetworkDespawn()
    {
        _activeValveId.OnValueChanged -= OnActiveValveChanged;
        _progress.OnValueChanged -= OnProgressChanged;
        _completed.OnValueChanged -= OnCompletedChanged;

        if (!_completed.Value)
            ApplyProgress(0);

        if (_rotationTarget != null)
        {
            _rotationTarget.localRotation = _initialRotation;
            _targetRotation = _initialRotation;
        }

        _interactionActive = false;
        _activeValve = null;
        SetBackgroundVisible(false);
    }

    private void Update()
    {
        AnimateRotation();

        if (IsServer && IsSpawned && !_completed.Value && _progress.Value > 0)
        {
            WaterValveInteractable serverValve = ResolveValve(_activeValveId.Value);
            if (serverValve == null ||
                Vector3.Distance(transform.position, serverValve.transform.position) > _interactionDistance + 1f)
            {
                _progress.Value = 0;
                _activeValveId.Value = 0;
            }
        }

        if (!IsOwner || !_interactionActive || _completed.Value)
            return;

        if (Input.GetKeyDown(KeyCode.Escape) ||
            _activeValve == null ||
            Vector3.Distance(transform.position, _activeValve.transform.position) > _interactionDistance)
        {
            CancelInteraction();
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            _hasTurnInput = true;
            _lastTurnInputTime = Time.time;
            TurnValveServerRpc(_activeValve.NetworkObject.NetworkObjectId);
        }
        else if (_hasTurnInput && Time.time - _lastTurnInputTime >= _idleResetDelay)
        {
            _hasTurnInput = false;
            _lastTurnInputTime = Time.time;
            ApplyProgress(0);
            ResetPartialProgressServerRpc(_activeValve.NetworkObject.NetworkObjectId);
        }
    }

    public void BeginInteraction(WaterValveInteractable valve)
    {
        if (!IsOwner || !IsSpawned || IsCompleted || valve == null ||
            valve.NetworkObject == null || !valve.NetworkObject.IsSpawned ||
            _rotationTarget == null)
            return;

        if (_interactionActive && _activeValve == valve)
            return;

        if (_interactionActive)
            CancelInteraction();

        _activeValve = valve;
        CacheRotationTarget(valve);
        _interactionActive = true;
        _hasTurnInput = false;
        _lastTurnInputTime = Time.time;
        _completionEventInvoked = false;
        SetBackgroundVisible(true);

        BeginInteractionServerRpc(valve.NetworkObject.NetworkObjectId);
    }

    public string GetInteractionPrompt(WaterValveInteractable valve)
    {
        if (valve == null)
            return string.Empty;
        if (_activeValve == valve && _completed.Value)
            return "Görev tamamlandı";
        if (_activeValve == valve && _interactionActive)
            return "Sol tıkla: Vanayı çevir (Esc: bırak)";
        return valve.InteractionPrompt;
    }

    public void OnLookTargetChanged(WaterValveInteractable lookedAtValve)
    {
        if (_interactionActive && _activeValve != lookedAtValve)
            CancelInteraction();
    }

    private void CancelInteraction()
    {
        if (!_interactionActive && _activeValve == null)
            return;

        ulong valveId = _activeValve != null && _activeValve.NetworkObject != null
            ? _activeValve.NetworkObject.NetworkObjectId
            : _activeValveId.Value;

        if (!_completed.Value)
            ApplyProgress(0);

        _interactionActive = false;
        _hasTurnInput = false;
        SetBackgroundVisible(false);

        if (IsOwner && IsSpawned && valveId != 0 && !_completed.Value)
            ResetInteractionServerRpc(valveId);
    }

    private void CacheRotationTarget(WaterValveInteractable valve)
    {
        _activeValve = valve;
        CacheRotationTarget();
    }

    private void CacheRotationTarget()
    {
        if (_rotationTarget != null || _valveSprite == null)
            return;

        _rotationTarget = _valveSprite.GetComponent<RectTransform>();
        if (_rotationTarget == null)
        {
            Debug.LogError("WaterValve: Valve Sprite must reference a UI object with a RectTransform.", this);
            return;
        }

        _initialRotation = _rotationTarget.localRotation;
        _targetRotation = _initialRotation;
    }

    private void AttachToValve(ulong valveId)
    {
        WaterValveInteractable valve = ResolveValve(valveId);
        if (valve == null)
            return;

        CacheRotationTarget(valve);
        ApplyProgress(_progress.Value);
    }

    private WaterValveInteractable ResolveValve(ulong valveId)
    {
        if (valveId == 0 || NetworkManager == null || NetworkManager.SpawnManager == null ||
            !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(valveId, out NetworkObject networkObject))
            return null;

        return networkObject.GetComponentInChildren<WaterValveInteractable>(true);
    }

    private void OnActiveValveChanged(ulong previous, ulong current)
    {
        _activeValve = null;

        if (current == 0)
        {
            if (!_completed.Value)
                SetRotation(0f);

            _interactionActive = false;
            SetBackgroundVisible(false);
            return;
        }

        AttachToValve(current);
        if (IsOwner && _interactionActive && !_completed.Value)
            SetBackgroundVisible(true);
    }

    private void OnProgressChanged(int previous, int current)
    {
        ApplyProgress(current);
    }

    private void OnCompletedChanged(bool previous, bool current)
    {
        if (current)
            CompleteTask();
    }

    private void ApplyProgress(int progress)
    {
        if (_rotationTarget == null)
            return;

        float normalizedProgress = Mathf.Clamp01(progress / (float)Mathf.Max(1, _clicksToComplete));
        SetRotation(_completionRotationDegrees * normalizedProgress);
    }

    private void SetRotation(float degrees)
    {
        if (_rotationTarget != null)
            _targetRotation = _initialRotation * Quaternion.AngleAxis(degrees, Vector3.forward);
    }

    private void AnimateRotation()
    {
        if (_rotationTarget == null)
            return;

        float duration = Mathf.Max(0.01f, _rotationAnimationDuration);
        float degreesPerSecond = Mathf.Max(180f, _completionRotationDegrees) / duration;
        _rotationTarget.localRotation = Quaternion.RotateTowards(
            _rotationTarget.localRotation,
            _targetRotation,
            degreesPerSecond * Time.deltaTime);
    }

    private void CompleteTask()
    {
        _interactionActive = false;
        _hasTurnInput = false;
        SetBackgroundVisible(false);

        if (_completionEventInvoked)
            return;

        _completionEventInvoked = true;
        _onTaskCompleted?.Invoke();
    }

    private void SetBackgroundVisible(bool visible)
    {
        if (_valveBackground == null || _valveBackground.transform == transform)
            return;

        if (_valveBackground.activeSelf != visible)
            _valveBackground.SetActive(visible);
    }

    private bool TryGetSenderPlayer(ulong senderClientId, out NetworkClient client)
    {
        client = null;
        return senderClientId == OwnerClientId &&
               NetworkManager != null &&
               NetworkManager.ConnectedClients.TryGetValue(senderClientId, out client) &&
               client.PlayerObject != null;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void BeginInteractionServerRpc(ulong valveId, RpcParams rpcParams = default)
    {
        if (!IsServer || !IsSpawned || _completed.Value ||
            !TryGetSenderPlayer(rpcParams.Receive.SenderClientId, out NetworkClient client))
            return;

        WaterValveInteractable valve = ResolveValve(valveId);
        if (valve == null ||
            Vector3.Distance(client.PlayerObject.transform.position, valve.transform.position) > _interactionDistance)
            return;

        if (_activeValveId.Value != valveId)
        {
            _progress.Value = 0;
            _activeValveId.Value = valveId;
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TurnValveServerRpc(ulong valveId, RpcParams rpcParams = default)
    {
        if (!IsServer || !IsSpawned || _completed.Value || _activeValveId.Value != valveId ||
            !TryGetSenderPlayer(rpcParams.Receive.SenderClientId, out NetworkClient client))
            return;

        WaterValveInteractable valve = ResolveValve(valveId);
        if (valve == null ||
            Vector3.Distance(client.PlayerObject.transform.position, valve.transform.position) > _interactionDistance)
            return;

        int targetProgress = Mathf.Max(1, _clicksToComplete);
        _progress.Value = Mathf.Min(_progress.Value + 1, targetProgress);
        if (_progress.Value >= targetProgress)
            _completed.Value = true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ResetPartialProgressServerRpc(ulong valveId, RpcParams rpcParams = default)
    {
        if (!IsServer || !IsSpawned || _completed.Value || _activeValveId.Value != valveId ||
            !TryGetSenderPlayer(rpcParams.Receive.SenderClientId, out NetworkClient client))
            return;

        WaterValveInteractable valve = ResolveValve(valveId);
        if (valve == null ||
            Vector3.Distance(client.PlayerObject.transform.position, valve.transform.position) > _interactionDistance)
            return;

        _progress.Value = 0;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ResetInteractionServerRpc(ulong valveId, RpcParams rpcParams = default)
    {
        if (!IsServer || !IsSpawned || _completed.Value || _activeValveId.Value != valveId ||
            !TryGetSenderPlayer(rpcParams.Receive.SenderClientId, out NetworkClient client))
            return;

        _progress.Value = 0;
        _activeValveId.Value = 0;
    }
}
