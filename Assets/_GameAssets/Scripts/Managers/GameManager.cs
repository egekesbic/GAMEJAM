using System;
using Unity.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(NetworkObject))]
[DisallowMultipleComponent]
public sealed class GameManager : NetworkBehaviour
{
    public struct ReplicatedTask : INetworkSerializable, IEquatable<ReplicatedTask>
    {
        public ulong NetworkObjectId;
        public byte JobTypeId;
        public FixedString512Bytes DisplayName;
        public bool IsCompleted;

        public ReplicatedTask(ulong networkObjectId, byte jobTypeId, string displayName, bool isCompleted)
        {
            NetworkObjectId = networkObjectId;
            JobTypeId = jobTypeId;
            DisplayName = default;
            DisplayName.CopyFromTruncated(displayName ?? string.Empty);
            IsCompleted = isCompleted;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref NetworkObjectId);
            serializer.SerializeValue(ref JobTypeId);
            serializer.SerializeValue(ref DisplayName);
            serializer.SerializeValue(ref IsCompleted);
        }

        public bool Equals(ReplicatedTask other)
        {
            return NetworkObjectId == other.NetworkObjectId &&
                   JobTypeId == other.JobTypeId &&
                   DisplayName.Equals(other.DisplayName) &&
                   IsCompleted == other.IsCompleted;
        }

        public override bool Equals(object obj)
        {
            return obj is ReplicatedTask other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = NetworkObjectId.GetHashCode();
                hash = (hash * 397) ^ JobTypeId.GetHashCode();
                hash = (hash * 397) ^ DisplayName.GetHashCode();
                return (hash * 397) ^ IsCompleted.GetHashCode();
            }
        }
    }

    public static GameManager Instance { get; private set; }

    [Header("Match")]
    [SerializeField, Min(1)] private int _matchDurationSeconds = 300;
    [SerializeField, Range(1, 100)] private int _requiredCompletionPercent = 80;

    private readonly NetworkVariable<int> _remainingSeconds = new NetworkVariable<int>(
        300,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> _matchEnded = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkList<ReplicatedTask> _replicatedTasks = new NetworkList<ReplicatedTask>(
        null,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private GameObject _timerHud;
    private TMP_Text _timerText;
    private JobManager _jobManager;
    private float _secondAccumulator;
    private bool _earlyWinCheckPending;

    public int RemainingSeconds => _remainingSeconds.Value;
    public NetworkList<ReplicatedTask> Tasks => _replicatedTasks;
    public event Action TasksChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("Birden fazla GameManager bulundu. GameScene'de tek bir GameManager olmalı.", this);
            return;
        }

        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _remainingSeconds.OnValueChanged += OnRemainingSecondsChanged;
        _replicatedTasks.OnListChanged += OnReplicatedTasksChanged;
        CreateTimerHud();
        RefreshTimerText(_remainingSeconds.Value);

        if (IsServer)
        {
            _remainingSeconds.Value = Mathf.Max(1, _matchDurationSeconds);
            _matchEnded.Value = false;
            _secondAccumulator = 0f;

            _jobManager = JobManager.Instance;
            if (_jobManager != null)
            {
                _jobManager.MatchProgressChanged += HandleMatchProgressChanged;
                _earlyWinCheckPending = true;
                SyncReplicatedTasks();
            }

            RefreshTimerText(_remainingSeconds.Value);
        }

        TasksChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        _remainingSeconds.OnValueChanged -= OnRemainingSecondsChanged;
        _replicatedTasks.OnListChanged -= OnReplicatedTasksChanged;
        if (_jobManager != null)
            _jobManager.MatchProgressChanged -= HandleMatchProgressChanged;

        DestroyTimerHud();
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        base.OnDestroy();
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned || _matchEnded.Value)
            return;

        if (_earlyWinCheckPending)
        {
            _earlyWinCheckPending = false;
            if (CheckForAllJobsCompleted())
                return;
        }

        _secondAccumulator += Time.unscaledDeltaTime;
        int elapsedSeconds = Mathf.FloorToInt(_secondAccumulator);
        if (elapsedSeconds <= 0)
            return;

        _secondAccumulator -= elapsedSeconds;
        _remainingSeconds.Value = Mathf.Max(0, _remainingSeconds.Value - elapsedSeconds);

        if (_remainingSeconds.Value <= 0)
            EndMatch();
    }

    private void HandleMatchProgressChanged()
    {
        _earlyWinCheckPending = true;
        if (IsServer && IsSpawned)
            SyncReplicatedTasks();
    }

    private void SyncReplicatedTasks()
    {
        if (!IsServer || !IsSpawned || _jobManager == null)
            return;

        System.Collections.Generic.List<JobManager.TaskSnapshot> snapshots = _jobManager.GetTaskSnapshots();
        bool hasChanged = snapshots.Count != _replicatedTasks.Count;
        if (!hasChanged)
        {
            for (int i = 0; i < snapshots.Count; i++)
            {
                JobManager.TaskSnapshot snapshot = snapshots[i];
                ReplicatedTask current = _replicatedTasks[i];
                if (current.NetworkObjectId != snapshot.NetworkObjectId ||
                    current.JobTypeId != (byte)snapshot.Type ||
                    current.DisplayName.ToString() != snapshot.DisplayName ||
                    current.IsCompleted != snapshot.IsCompleted)
                {
                    hasChanged = true;
                    break;
                }
            }
        }

        if (!hasChanged)
            return;

        _replicatedTasks.Clear();
        foreach (JobManager.TaskSnapshot snapshot in snapshots)
        {
            _replicatedTasks.Add(new ReplicatedTask(
                snapshot.NetworkObjectId,
                (byte)snapshot.Type,
                snapshot.DisplayName,
                snapshot.IsCompleted));
        }
    }

    private void OnReplicatedTasksChanged(NetworkListEvent<ReplicatedTask> changeEvent)
    {
        TasksChanged?.Invoke();
    }

    private bool CheckForAllJobsCompleted()
    {
        if (_jobManager == null || _matchEnded.Value)
            return false;

        _jobManager.GetMatchProgress(out int completedJobs, out int totalJobs);
        if (totalJobs <= 0 || completedJobs < totalJobs)
            return false;

        FinishMatch(true, completedJobs, totalJobs, "Tüm görevler tamamlandı");
        return true;
    }

    private void EndMatch()
    {
        if (_matchEnded.Value)
            return;

        int completedJobs = 0;
        int totalJobs = 0;
        if (_jobManager == null)
            Debug.LogError("GameManager: JobManager bulunamadı; maç kayıp sayılacak.", this);
        else
            _jobManager.GetMatchProgress(out completedJobs, out totalJobs);

        bool won = totalJobs > 0 &&
                   (long)completedJobs * 100 >= (long)totalJobs * _requiredCompletionPercent;
        FinishMatch(won, completedJobs, totalJobs, "Süre doldu");
    }

    private void FinishMatch(bool won, int completedJobs, int totalJobs, string reason)
    {
        if (_matchEnded.Value)
            return;

        _matchEnded.Value = true;
        string resultScene = won ? "WinScene" : "LoseScene";

        Debug.Log(
            $"[GameManager] {reason}: {completedJobs}/{totalJobs} görev tamamlandı " +
            $"({_requiredCompletionPercent}% gerekli). {(won ? "Kazandınız" : "Kaybettiniz")}: {resultScene}",
            this);

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || networkManager.SceneManager == null)
        {
            Debug.LogError("GameManager: Netcode Scene Management kapalı; sonuç sahnesi yüklenemedi.", this);
            return;
        }

        SceneEventProgressStatus status = networkManager.SceneManager.LoadScene(
            resultScene,
            LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
            Debug.LogError($"GameManager: {resultScene} yüklenemedi. Netcode durumu: {status}", this);
    }

    private void OnRemainingSecondsChanged(int previousValue, int newValue)
    {
        RefreshTimerText(newValue);
    }

    private void RefreshTimerText(int seconds)
    {
        if (_timerText == null)
            return;

        int minutes = Mathf.Max(0, seconds) / 60;
        int remainingSeconds = Mathf.Max(0, seconds) % 60;
        _timerText.text = $"{minutes:00}:{remainingSeconds:00}";
    }

    private void CreateTimerHud()
    {
        if (_timerHud != null)
            return;

        _timerHud = new GameObject("GameTimerHUD");
        Canvas canvas = _timerHud.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = _timerHud.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        _timerHud.AddComponent<GraphicRaycaster>();

        GameObject background = new GameObject("TimerBackground", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(_timerHud.transform, false);

        RectTransform backgroundRect = (RectTransform)background.transform;
        backgroundRect.anchorMin = new Vector2(0.5f, 1f);
        backgroundRect.anchorMax = new Vector2(0.5f, 1f);
        backgroundRect.pivot = new Vector2(0.5f, 1f);
        backgroundRect.anchoredPosition = new Vector2(0f, -24f);
        backgroundRect.sizeDelta = new Vector2(240f, 72f);
        background.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

        GameObject label = new GameObject("TimerText", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(background.transform, false);

        RectTransform labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        _timerText = label.GetComponent<TextMeshProUGUI>();
        _timerText.alignment = TextAlignmentOptions.Center;
        _timerText.fontSize = 46f;
        _timerText.fontStyle = FontStyles.Bold;
        _timerText.color = Color.white;
        _timerText.raycastTarget = false;
    }

    private void DestroyTimerHud()
    {
        if (_timerHud == null)
            return;

        Destroy(_timerHud);
        _timerHud = null;
        _timerText = null;
    }
}
