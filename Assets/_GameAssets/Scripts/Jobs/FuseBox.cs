using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class FuseBox : NetworkBehaviour, IPlayerInteractable
{
    [Header("UI")]
    [SerializeField] private GameObject _panelRoot;
    [SerializeField] private Button[] _buttons = new Button[6];
    [SerializeField] private Button _mainSwitchButton;

    [Header("Fuse Sprites")]
    [SerializeField] private Sprite _onSprite;
    [SerializeField] private Sprite _offSprite;

    [Header("Main Switch Sprites")]
    [SerializeField] private Sprite _mainSwitchOnSprite;
    [SerializeField] private Sprite _mainSwitchOffSprite;

    [Header("Malfunction Settings")]
    [SerializeField, Range(0f, 1f)] private float _initialMalfunctionChance = 0.7f;
    [SerializeField, Range(0f, 1f)] private float _malfunctionReduction = 0.1f;
    [SerializeField, Min(0)] private int _maxAffectedNeighbors = 2;
    [SerializeField, Min(0.1f)] private float _interactionDistance = 3.5f;
    [SerializeField] private string _openPrompt = "F - Sigorta kutusunu aç";

    private NetworkList<bool> _fuseStates;
    private readonly NetworkVariable<bool> _mainSwitchOff = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> _completed = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private static FuseBox _sharedInstance;
    private UnityAction[] _fuseButtonListeners;
    private UnityAction _mainSwitchButtonListener;
    private Canvas _panelCanvas;
    private float _malfunctionChance;
    private bool _initialized;
    private bool _savedCursorState;
    private bool _previousCursorVisible;
    private CursorLockMode _previousCursorLockMode;
    private bool _savedPanelCanvasState;
    private bool _previousPanelCanvasActive;
    private bool _previousPanelCanvasEnabled;
    private bool _previousPanelCanvasOverrideSorting;
    private int _previousPanelCanvasSortingOrder;
    private RenderMode _previousPanelCanvasRenderMode;
    private Camera _previousPanelCanvasCamera;
    private Vector3 _previousPanelCanvasScale;

    public static FuseBox SharedInstance
    {
        get
        {
            if (_sharedInstance != null && _sharedInstance.IsSpawned)
                return _sharedInstance;

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || networkManager.SpawnManager == null)
                return null;

            foreach (NetworkObject networkObject in networkManager.SpawnManager.SpawnedObjectsList)
            {
                if (networkObject == null || !networkObject.IsPlayerObject ||
                    networkObject.OwnerClientId != NetworkManager.ServerClientId)
                    continue;

                FuseBox sharedFuseBox = networkObject.GetComponent<FuseBox>();
                if (sharedFuseBox != null && sharedFuseBox.IsSpawned)
                {
                    _sharedInstance = sharedFuseBox;
                    return _sharedInstance;
                }
            }

            return null;
        }
    }

    public static bool IsAnyPanelOpen => SharedInstance != null && SharedInstance.IsPanelOpen;
    public bool IsCompleted => _completed.Value;
    public bool IsPanelOpen => _panelRoot != null && _panelRoot.activeSelf;
    public bool CanOpenPanel => IsClient && IsSpawned && _panelRoot != null && !_panelRoot.activeSelf;
    public string OpenPrompt => _openPrompt;
    public Sprite OnSprite => _onSprite;
    public Sprite OffSprite => _offSprite;
    public Sprite MainSwitchOnSprite => _mainSwitchOnSprite;
    public Sprite MainSwitchOffSprite => _mainSwitchOffSprite;
    public bool CanTurnOffMainSwitch =>
        !_completed.Value && !_mainSwitchOff.Value && AreAllFusesOff();
    public string InteractionPrompt => _panelRoot != null && _panelRoot.activeSelf
        ? string.Empty
        : _openPrompt;

    private void Awake()
    {
        _fuseStates = new NetworkList<bool>();
        if (_panelRoot != null)
            _panelCanvas = _panelRoot.GetComponentInParent<Canvas>(true);

        if (_panelRoot != null)
            _panelRoot.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        if (NetworkObject.IsPlayerObject && OwnerClientId == NetworkManager.ServerClientId)
            _sharedInstance = this;

        _fuseStates.OnListChanged += OnFuseStatesChanged;
        _mainSwitchOff.OnValueChanged += OnMainSwitchChanged;
        _completed.OnValueChanged += OnCompletedChanged;
        BindUiButtons();

        if (IsServer)
        {
            _malfunctionChance = _initialMalfunctionChance;
            _mainSwitchOff.Value = false;
            _completed.Value = false;
            _fuseStates.Clear();

            int fuseCount = _buttons != null ? _buttons.Length : 0;
            for (int i = 0; i < fuseCount; i++)
                _fuseStates.Add(true);

            _initialized = true;
        }

        RefreshUI();
    }

    public override void OnNetworkDespawn()
    {
        UnbindUiButtons();
        _fuseStates.OnListChanged -= OnFuseStatesChanged;
        _mainSwitchOff.OnValueChanged -= OnMainSwitchChanged;
        _completed.OnValueChanged -= OnCompletedChanged;
        ClosePanel();
        _initialized = false;

        if (_sharedInstance == this)
            _sharedInstance = null;
    }

    private void Update()
    {
        if (IsClient && _panelRoot != null && _panelRoot.activeSelf &&
            Input.GetKeyDown(KeyCode.Escape))
            ClosePanel();
    }

    public void Interact(Vector3 rayOrigin, Vector3 rayDirection)
    {
        OpenPanel();
    }

    public void OpenPanel()
    {
        if (!CanOpenPanel)
            return;

        EnsureEventSystem();
        PreparePanelCanvas();

        _previousCursorVisible = Cursor.visible;
        _previousCursorLockMode = Cursor.lockState;
        _savedCursorState = true;
        _panelRoot.SetActive(true);
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public void ClosePanel()
    {
        if (_panelRoot == null)
            return;

        _panelRoot.SetActive(false);
        if (_savedCursorState)
        {
            Cursor.visible = _previousCursorVisible;
            Cursor.lockState = _previousCursorLockMode;
            _savedCursorState = false;
        }

        RestorePanelCanvas();
    }

    private void PreparePanelCanvas()
    {
        if (_panelCanvas == null && _panelRoot != null)
            _panelCanvas = _panelRoot.GetComponentInParent<Canvas>(true);

        if (_panelCanvas == null)
            return;

        if (!_savedPanelCanvasState)
        {
            _previousPanelCanvasActive = _panelCanvas.gameObject.activeSelf;
            _previousPanelCanvasEnabled = _panelCanvas.enabled;
            _previousPanelCanvasOverrideSorting = _panelCanvas.overrideSorting;
            _previousPanelCanvasSortingOrder = _panelCanvas.sortingOrder;
            _previousPanelCanvasRenderMode = _panelCanvas.renderMode;
            _previousPanelCanvasCamera = _panelCanvas.worldCamera;
            _previousPanelCanvasScale = _panelCanvas.transform.localScale;
            _savedPanelCanvasState = true;
        }

        // The shared panel lives on the host player's prefab. That player's Canvas is
        // disabled on other clients, so make it a visible local overlay while open.
        _panelCanvas.gameObject.SetActive(true);
        _panelCanvas.enabled = true;
        _panelCanvas.transform.localScale = Vector3.one;
        _panelCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _panelCanvas.worldCamera = null;
        _panelCanvas.overrideSorting = true;
        _panelCanvas.sortingOrder = Mathf.Max(100, _panelCanvas.sortingOrder);
    }

    private void RestorePanelCanvas()
    {
        if (!_savedPanelCanvasState || _panelCanvas == null)
            return;

        _panelCanvas.renderMode = _previousPanelCanvasRenderMode;
        _panelCanvas.worldCamera = _previousPanelCanvasCamera;
        _panelCanvas.overrideSorting = _previousPanelCanvasOverrideSorting;
        _panelCanvas.sortingOrder = _previousPanelCanvasSortingOrder;
        _panelCanvas.transform.localScale = _previousPanelCanvasScale;
        _panelCanvas.enabled = _previousPanelCanvasEnabled;
        _panelCanvas.gameObject.SetActive(_previousPanelCanvasActive);
        _savedPanelCanvasState = false;
    }

    private static void EnsureEventSystem()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            GameObject eventSystemObject = new GameObject("EventSystem");
            eventSystem = eventSystemObject.AddComponent<EventSystem>();
        }

        if (eventSystem.GetComponent<BaseInputModule>() == null)
        {
            InputSystemUIInputModule inputModule =
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();
        }
    }

    private void BindUiButtons()
    {
        UnbindUiButtons();

        if (_buttons != null)
        {
            _fuseButtonListeners = new UnityAction[_buttons.Length];
            for (int i = 0; i < _buttons.Length; i++)
            {
                if (_buttons[i] == null)
                    continue;

                int fuseIndex = i;
                UnityAction listener = () => RequestFuseInteraction(fuseIndex);
                _fuseButtonListeners[i] = listener;
                _buttons[i].onClick.AddListener(listener);
            }
        }

        if (_mainSwitchButton != null)
        {
            _mainSwitchButtonListener = RequestMainSwitchOff;
            _mainSwitchButton.onClick.AddListener(_mainSwitchButtonListener);
        }
    }

    private void UnbindUiButtons()
    {
        if (_buttons != null && _fuseButtonListeners != null)
        {
            int count = Mathf.Min(_buttons.Length, _fuseButtonListeners.Length);
            for (int i = 0; i < count; i++)
            {
                if (_buttons[i] == null || _fuseButtonListeners[i] == null)
                    continue;

                _buttons[i].onClick.RemoveListener(_fuseButtonListeners[i]);
            }
        }
        _fuseButtonListeners = null;

        if (_mainSwitchButton != null && _mainSwitchButtonListener != null)
            _mainSwitchButton.onClick.RemoveListener(_mainSwitchButtonListener);
        _mainSwitchButtonListener = null;
    }

    public bool CanInteractWithFuse(int index)
    {
        return IsSpawned && !_completed.Value && !_mainSwitchOff.Value &&
               _fuseStates != null && index >= 0 && index < _fuseStates.Count &&
               _fuseStates[index];
    }

    public void RequestFuseInteraction(int index)
    {
        if (IsSpawned && _buttons != null &&
            index >= 0 && index < _buttons.Length && _buttons[index] != null &&
            CanInteractWithFuse(index))
            InteractServerRpc(index);
    }

    // Compatibility overload for the earlier camera-ray interaction script.
    public void RequestFuseInteraction(int index, Vector3 rayOrigin, Vector3 rayDirection)
    {
        RequestFuseInteraction(index);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void InteractServerRpc(int index, RpcParams rpcParams = default)
    {
        if (!IsServer || !IsSpawned || !_initialized || _completed.Value ||
            _mainSwitchOff.Value || index < 0 || index >= _fuseStates.Count ||
            !_fuseStates[index])
            return;

        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!TryGetNearbyPlayer(clientId, out _))
            return;

        // The requested UI button's index is the only fuse switched by this interaction.
        _fuseStates[index] = false;
        _malfunctionChance = Mathf.Max(0f, _malfunctionChance - _malfunctionReduction);
        AffectNeighbors(index);
        RefreshUI();
    }

    public bool CanUseMainSwitch => CanTurnOffMainSwitch && _mainSwitchButton != null;

    public bool IsMainSwitchButton(GameObject candidate)
    {
        return candidate != null && _mainSwitchButton != null &&
               (candidate == _mainSwitchButton.gameObject ||
                candidate.transform.IsChildOf(_mainSwitchButton.transform));
    }

    public void RequestMainSwitchOff()
    {
        if (IsSpawned && CanUseMainSwitch)
            MainSwitchServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void MainSwitchServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer || !IsSpawned || !_initialized || !CanTurnOffMainSwitch ||
            !TryGetNearbyPlayer(rpcParams.Receive.SenderClientId, out _))
            return;

        _mainSwitchOff.Value = true;
        _completed.Value = true;
        OnFuseBoxCompleted();
        RefreshUI();
    }

    private bool TryGetNearbyPlayer(ulong clientId, out NetworkClient client)
    {
        client = null;
        if (NetworkManager == null ||
            !NetworkManager.ConnectedClients.TryGetValue(clientId, out client) ||
            client.PlayerObject == null)
            return false;

        return Vector3.Distance(client.PlayerObject.transform.position, GetWorldInteractionPosition()) <=
               _interactionDistance;
    }

    private Vector3 GetWorldInteractionPosition()
    {
        FuseButton[] fuseButtons = FindObjectsByType<FuseButton>(FindObjectsInactive.Exclude);
        foreach (FuseButton fuseButton in fuseButtons)
        {
            if (fuseButton != null && fuseButton.IsWorldInteraction)
                return fuseButton.transform.position;
        }

        return transform.position;
    }

    private bool AreAllFusesOff()
    {
        if (_fuseStates == null || _fuseStates.Count == 0)
            return false;

        for (int i = 0; i < _fuseStates.Count; i++)
            if (_fuseStates[i])
                return false;

        return true;
    }

    private void AffectNeighbors(int index)
    {
        List<int> neighbors = new List<int>();
        if (index > 0) neighbors.Add(index - 1);
        if (index < _fuseStates.Count - 1) neighbors.Add(index + 1);

        for (int i = 0; i < neighbors.Count; i++)
        {
            int randomIndex = Random.Range(i, neighbors.Count);
            (neighbors[i], neighbors[randomIndex]) = (neighbors[randomIndex], neighbors[i]);
        }

        int affectedCount = 0;
        foreach (int neighbor in neighbors)
        {
            if (affectedCount >= _maxAffectedNeighbors) break;
            if (Random.value > _malfunctionChance) continue;
            _fuseStates[neighbor] = !_fuseStates[neighbor];
            affectedCount++;
        }
    }

    private void OnFuseStatesChanged(NetworkListEvent<bool> changeEvent) => RefreshUI();

    private void OnMainSwitchChanged(bool previous, bool current) => RefreshUI();

    private void OnCompletedChanged(bool previous, bool current)
    {
        RefreshUI();
        if (current)
            OnFuseBoxCompletedClient();
    }

    private void RefreshUI()
    {
        if (_fuseStates != null && _buttons != null)
        {
            int count = Mathf.Min(_fuseStates.Count, _buttons.Length);
            for (int i = 0; i < count; i++)
            {
                if (_buttons[i] != null && _buttons[i].image != null)
                    _buttons[i].image.sprite = _fuseStates[i] ? _onSprite : _offSprite;

                if (_buttons[i] != null)
                    _buttons[i].interactable = CanInteractWithFuse(i);
            }
        }

        if (_mainSwitchButton != null)
        {
            _mainSwitchButton.interactable = CanUseMainSwitch;
            if (_mainSwitchButton.image != null)
                _mainSwitchButton.image.sprite = _mainSwitchOff.Value
                    ? _mainSwitchOffSprite
                    : _mainSwitchOnSprite;
        }
    }

    private void OnFuseBoxCompleted()
    {
        // Server-only hook for opening the room door or advancing the objective.
    }

    private void OnFuseBoxCompletedClient()
    {
        // Client-side completion feedback hook.
    }
}
