using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(NetworkObject))]
[DisallowMultipleComponent]
public sealed class GameManager : NetworkBehaviour
{
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

    private GameObject _timerHud;
    private TMP_Text _timerText;
    private float _secondAccumulator;

    public int RemainingSeconds => _remainingSeconds.Value;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _remainingSeconds.OnValueChanged += OnRemainingSecondsChanged;
        CreateTimerHud();
        RefreshTimerText(_remainingSeconds.Value);

        if (IsServer)
        {
            _remainingSeconds.Value = Mathf.Max(1, _matchDurationSeconds);
            _matchEnded.Value = false;
            _secondAccumulator = 0f;
            RefreshTimerText(_remainingSeconds.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        _remainingSeconds.OnValueChanged -= OnRemainingSecondsChanged;
        DestroyTimerHud();
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned || _matchEnded.Value)
            return;

        _secondAccumulator += Time.unscaledDeltaTime;
        int elapsedSeconds = Mathf.FloorToInt(_secondAccumulator);
        if (elapsedSeconds <= 0)
            return;

        _secondAccumulator -= elapsedSeconds;
        _remainingSeconds.Value = Mathf.Max(0, _remainingSeconds.Value - elapsedSeconds);

        if (_remainingSeconds.Value <= 0)
            EndMatch();
    }

    private void EndMatch()
    {
        if (_matchEnded.Value)
            return;

        _matchEnded.Value = true;

        int completedJobs = 0;
        int totalJobs = 0;
        if (JobManager.Instance == null)
            Debug.LogError("GameManager: JobManager bulunamadı; maç kayıp sayılacak.", this);
        else
            JobManager.Instance.GetMatchProgress(out completedJobs, out totalJobs);

        bool won = totalJobs > 0 &&
                   (long)completedJobs * 100 >= (long)totalJobs * _requiredCompletionPercent;
        string resultScene = won ? "WinScene" : "LoseScene";

        Debug.Log(
            $"[GameManager] Süre doldu: {completedJobs}/{totalJobs} görev tamamlandı " +
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
