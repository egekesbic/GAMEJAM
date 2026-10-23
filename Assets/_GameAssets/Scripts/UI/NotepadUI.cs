using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class NotepadUI : MonoBehaviour
{
    [Header("Notepad Appearance")]
    [SerializeField] private Sprite _notepadSprite;
    [SerializeField] private Sprite _completedSprite;
    [SerializeField] private TMP_FontAsset _fontAsset;

    [SerializeField, Min(1)]
    private int _tasksPerPage = 13;

    [SerializeField]
    private KeyCode _pageKey = KeyCode.Q;

    [SerializeField]
    private Vector2 _notepadSize = new Vector2(430f, 570f);

    [SerializeField]
    private Vector2 _screenOffset = new Vector2(-30f, -30f);

    [Header("Task Names - Editable in Inspector")]
    [SerializeField]
    private string _windowRepairTaskName = "Window Repair";

    [SerializeField]
    private string _fuseBoxRepairTaskName = "Fuse Box Repair";

    [Tooltip("Use {0} for the task name and {1} for its number.")]
    [SerializeField]
    private string _fuseBoxMultipleFormat = "{0} {1}";

    [SerializeField]
    private string _waterValveRepairTaskName = "Water Valve Repair";

    [Tooltip("Use {0} for the task name and {1} for its number.")]
    [SerializeField]
    private string _waterValveMultipleFormat = "{0} {1}";

    [SerializeField]
    private string _itemDeliveryTaskName = "Item Delivery";

    [Tooltip("Use {0} for the task name and {1} for its number.")]
    [SerializeField]
    private string _itemDeliveryMultipleFormat = "{0} {1}";

    [SerializeField]
    private string _otherTaskName = "Complete Task";

    private static readonly Color PaperColor =
        new Color(0.95f, 0.94f, 0.89f, 1f);

    private static readonly Color TextColor =
        new Color(0.16f, 0.19f, 0.22f, 1f);

    private static readonly Color MutedTextColor =
        new Color(0.43f, 0.47f, 0.49f, 1f);

    private static readonly Color CompletedColor =
        new Color(0.20f, 0.46f, 0.31f, 1f);

    private static readonly Color SeparatorColor =
        new Color(0.28f, 0.32f, 0.34f, 0.18f);

    private sealed class TaskRow
    {
        public GameObject Root;
        public TMP_Text Number;
        public TMP_Text Label;
        public TMP_Text Progress;

        public Image CheckBackground;
        public Outline CheckOutline;
        public Image CompletionImage;
        public TMP_Text FallbackCheck;
    }

    private sealed class DisplayTask
    {
        public string Label;
        public string Progress;
        public bool IsCompleted;

        public DisplayTask(string label, string progress, bool isCompleted)
        {
            Label = label;
            Progress = progress;
            IsCompleted = isCompleted;
        }
    }

    private readonly List<TaskRow> _rows = new List<TaskRow>();

    private GameObject _canvasObject;
    private GameManager _gameManager;
    private int _currentPage;

    // The task rows now use the space previously occupied by the header/footer.
    private const float BodyTop = 0.94f;
    private const float BodyBottom = 0.06f;

    private void Awake()
    {
        _tasksPerPage = Mathf.Max(1, _tasksPerPage);

        _notepadSize.x = Mathf.Max(250f, _notepadSize.x);
        _notepadSize.y = Mathf.Max(350f, _notepadSize.y);

        CreateNotepad();
    }

    private void OnEnable()
    {
        if (_canvasObject != null)
            _canvasObject.SetActive(true);

        BindToGameManager();
    }

    private void OnDisable()
    {
        UnbindFromGameManager();

        if (_canvasObject != null)
            _canvasObject.SetActive(false);
    }

    private void OnDestroy()
    {
        UnbindFromGameManager();

        if (_canvasObject != null)
            Destroy(_canvasObject);
    }

    private void Update()
    {
        BindToGameManager();

        if (Input.GetKeyDown(_pageKey))
            ShowNextPage();
    }

    private void BindToGameManager()
    {
        GameManager current = GameManager.Instance;

        if (current == _gameManager)
            return;

        UnbindFromGameManager();

        _gameManager = current;

        if (_gameManager != null)
            _gameManager.TasksChanged += RefreshPage;

        RefreshPage();
    }

    private void UnbindFromGameManager()
    {
        if (_gameManager != null)
            _gameManager.TasksChanged -= RefreshPage;

        _gameManager = null;
    }

    private void ShowNextPage()
    {
        if (_gameManager == null || !_gameManager.IsSpawned)
            return;

        List<DisplayTask> tasks =
            BuildDisplayTasks(_gameManager.Tasks);

        int pageCount = GetPageCount(tasks.Count);

        if (pageCount <= 1)
            return;

        _currentPage = (_currentPage + 1) % pageCount;

        RefreshPage();
    }

    private void RefreshPage()
    {
        if (_canvasObject == null)
            return;

        if (_gameManager == null || !_gameManager.IsSpawned)
        {
            HideAllRows();
            return;
        }

        List<DisplayTask> tasks =
            BuildDisplayTasks(_gameManager.Tasks);

        int totalTasks = tasks.Count;
        int pageCount = GetPageCount(totalTasks);

        if (pageCount == 0)
        {
            _currentPage = 0;
            HideAllRows();
            return;
        }

        _currentPage = Mathf.Clamp(
            _currentPage,
            0,
            pageCount - 1);

        int firstTaskIndex = _currentPage * _tasksPerPage;

        for (int rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            int taskIndex = firstTaskIndex + rowIndex;
            TaskRow row = _rows[rowIndex];

            bool hasTask = taskIndex < totalTasks;

            row.Root.SetActive(hasTask);

            if (!hasTask)
                continue;

            DisplayTask task = tasks[taskIndex];

            row.Number.text = (taskIndex + 1).ToString("00");
            row.Label.text = task.Label;
            row.Progress.text = task.Progress;

            row.Label.fontStyle = task.IsCompleted
                ? FontStyles.Strikethrough
                : FontStyles.Normal;

            row.Label.color = task.IsCompleted
                ? CompletedColor
                : TextColor;

            row.Progress.color = task.IsCompleted
                ? CompletedColor
                : MutedTextColor;

            row.CheckBackground.color = task.IsCompleted
                ? new Color(0.88f, 0.94f, 0.87f, 1f)
                : PaperColor;

            row.CheckOutline.effectColor = task.IsCompleted
                ? CompletedColor
                : new Color(0.42f, 0.45f, 0.45f, 0.75f);

            bool hasCompletionSprite = _completedSprite != null;

            row.CompletionImage.gameObject.SetActive(
                task.IsCompleted && hasCompletionSprite);

            row.FallbackCheck.gameObject.SetActive(
                task.IsCompleted && !hasCompletionSprite);
        }
    }

    private void HideAllRows()
    {
        foreach (TaskRow row in _rows)
            row.Root.SetActive(false);
    }

    private List<DisplayTask> BuildDisplayTasks(
        NetworkList<GameManager.ReplicatedTask> tasks)
    {
        List<DisplayTask> displayTasks =
            new List<DisplayTask>();

        int totalWindows = 0;
        int completedWindows = 0;

        // Combine all window repairs into one progress entry.
        for (int i = 0; i < tasks.Count; i++)
        {
            GameManager.ReplicatedTask task = tasks[i];

            if ((JobManager.JobType)task.JobTypeId
                != JobManager.JobType.UpgradeWindow)
            {
                continue;
            }

            totalWindows++;

            if (task.IsCompleted)
                completedWindows++;
        }

        if (totalWindows > 0)
        {
            displayTasks.Add(new DisplayTask(
                GetName(_windowRepairTaskName, "Window Repair"),
                $"{completedWindows}/{totalWindows}",
                completedWindows >= totalWindows));
        }

        // Display other tasks individually.
        for (int i = 0; i < tasks.Count; i++)
        {
            GameManager.ReplicatedTask task = tasks[i];

            JobManager.JobType type =
                (JobManager.JobType)task.JobTypeId;

            if (type == JobManager.JobType.UpgradeWindow)
                continue;

            int ordinal =
                GetTypeOrdinal(tasks, i, task.JobTypeId) + 1;

            int typeCount =
                CountTasksOfType(tasks, task.JobTypeId);

            string label;

            switch (type)
            {
                case JobManager.JobType.FuseBox:
                    label = GetTaskLabel(
                        _fuseBoxRepairTaskName,
                        _fuseBoxMultipleFormat,
                        ordinal,
                        typeCount,
                        "Fuse Box Repair");
                    break;

                case JobManager.JobType.WaterValve:
                    label = GetTaskLabel(
                        _waterValveRepairTaskName,
                        _waterValveMultipleFormat,
                        ordinal,
                        typeCount,
                        "Water Valve Repair");
                    break;

                case JobManager.JobType.ItemDelivery:
                    label = GetTaskLabel(
                        _itemDeliveryTaskName,
                        _itemDeliveryMultipleFormat,
                        ordinal,
                        typeCount,
                        "Item Delivery");
                    break;

                default:
                    label = GetName(
                        _otherTaskName,
                        "Complete Task");
                    break;
            }

            displayTasks.Add(new DisplayTask(
                label,
                string.Empty,
                task.IsCompleted));
        }

        return displayTasks;
    }

    private static string GetName(
        string value,
        string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }

    private static string GetTaskLabel(
        string taskName,
        string multipleFormat,
        int ordinal,
        int typeCount,
        string fallback)
    {
        string name = GetName(taskName, fallback);

        if (typeCount <= 1)
            return name;

        string format = string.IsNullOrWhiteSpace(multipleFormat)
            ? "{0} {1}"
            : multipleFormat;

        try
        {
            return string.Format(format, name, ordinal);
        }
        catch (FormatException)
        {
            return $"{name} {ordinal}";
        }
    }

    private static int GetTypeOrdinal(
        NetworkList<GameManager.ReplicatedTask> tasks,
        int taskIndex,
        byte typeId)
    {
        int ordinal = 0;

        for (int i = 0; i < taskIndex; i++)
        {
            if (tasks[i].JobTypeId == typeId)
                ordinal++;
        }

        return ordinal;
    }

    private static int CountTasksOfType(
        NetworkList<GameManager.ReplicatedTask> tasks,
        byte typeId)
    {
        int count = 0;

        for (int i = 0; i < tasks.Count; i++)
        {
            if (tasks[i].JobTypeId == typeId)
                count++;
        }

        return count;
    }

    private int GetPageCount(int totalTasks)
    {
        return totalTasks <= 0
            ? 0
            : Mathf.CeilToInt(
                (float)totalTasks / _tasksPerPage);
    }

    // --------------------------------------------------
    // UI CREATION
    // --------------------------------------------------

    private void CreateNotepad()
    {
        _canvasObject = new GameObject(
            "NotepadCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));

        Canvas canvas = _canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;

        CanvasScaler scaler =
            _canvasObject.GetComponent<CanvasScaler>();

        scaler.uiScaleMode =
            CanvasScaler.ScaleMode.ScaleWithScreenSize;

        scaler.referenceResolution =
            new Vector2(1920f, 1080f);

        scaler.matchWidthOrHeight = 0.5f;

        GameObject paper = CreateUIObject(
            "NotepadPaper",
            _canvasObject.transform,
            typeof(Image));

        RectTransform paperRect =
            paper.GetComponent<RectTransform>();

        paperRect.anchorMin = Vector2.one;
        paperRect.anchorMax = Vector2.one;
        paperRect.pivot = Vector2.one;
        paperRect.anchoredPosition = _screenOffset;
        paperRect.sizeDelta = _notepadSize;

        Image paperImage = paper.GetComponent<Image>();

        paperImage.sprite = _notepadSprite;
        paperImage.color =
            _notepadSprite != null ? Color.white : PaperColor;

        paperImage.preserveAspect = _notepadSprite != null;
        paperImage.raycastTarget = false;

        Shadow shadow = paper.AddComponent<Shadow>();

        shadow.effectColor =
            new Color(0f, 0f, 0f, 0.22f);

        shadow.effectDistance = new Vector2(-5f, -5f);
        shadow.useGraphicAlpha = true;

        // No header or footer: only the task rows are created.
        for (int i = 0; i < _tasksPerPage; i++)
            _rows.Add(CreateTaskRow(paper.transform, i));
    }

    private TaskRow CreateTaskRow(
        Transform parent,
        int index)
    {
        GameObject rowObject = new GameObject(
            $"TaskRow_{index + 1}",
            typeof(RectTransform),
            typeof(Image));

        rowObject.transform.SetParent(parent, false);

        float bodyHeight = BodyTop - BodyBottom;
        float slotHeight = bodyHeight / _tasksPerPage;
        float rowHeight = slotHeight * 0.90f;

        float centerY =
            BodyTop - slotHeight * (index + 0.5f);

        RectTransform rowRect =
            rowObject.GetComponent<RectTransform>();

        rowRect.anchorMin = new Vector2(
            0.055f,
            centerY - rowHeight * 0.5f);

        rowRect.anchorMax = new Vector2(
            0.945f,
            centerY + rowHeight * 0.5f);

        rowRect.offsetMin = Vector2.zero;
        rowRect.offsetMax = Vector2.zero;

        Image background = rowObject.GetComponent<Image>();

        background.color = index % 2 == 0
            ? new Color(0.22f, 0.28f, 0.30f, 0.035f)
            : new Color(0.22f, 0.28f, 0.30f, 0.075f);

        background.raycastTarget = false;

        // Task number.
        TMP_Text number = CreateText(
            "TaskNumber",
            rowObject.transform,
            new Vector2(0.005f, 0f),
            new Vector2(0.085f, 1f),
            10f,
            FontStyles.Bold,
            MutedTextColor,
            TextAlignmentOptions.MidlineLeft);

        number.text = (index + 1).ToString("00");

        // Checkbox background.
        GameObject checkObject = CreateUIObject(
            "CompletionBox",
            rowObject.transform,
            typeof(Image));

        RectTransform checkRect =
            checkObject.GetComponent<RectTransform>();

        checkRect.anchorMin =
            new Vector2(0.105f, 0.5f);

        checkRect.anchorMax =
            new Vector2(0.105f, 0.5f);

        checkRect.pivot = new Vector2(0.5f, 0.5f);
        checkRect.anchoredPosition = Vector2.zero;
        checkRect.sizeDelta = new Vector2(17f, 17f);

        Image checkBackground =
            checkObject.GetComponent<Image>();

        checkBackground.color = PaperColor;
        checkBackground.raycastTarget = false;

        Outline checkOutline =
            checkObject.AddComponent<Outline>();

        checkOutline.effectColor =
            new Color(0.42f, 0.45f, 0.45f, 0.75f);

        checkOutline.effectDistance = new Vector2(1f, 1f);
        checkOutline.useGraphicAlpha = true;

        // Optional completion sprite.
        GameObject completionMark = CreateUIObject(
            "CompletionMark",
            checkObject.transform,
            typeof(Image));

        RectTransform markRect =
            completionMark.GetComponent<RectTransform>();

        markRect.anchorMin = Vector2.zero;
        markRect.anchorMax = Vector2.one;
        markRect.offsetMin = new Vector2(2f, 2f);
        markRect.offsetMax = new Vector2(-2f, -2f);

        Image completionImage =
            completionMark.GetComponent<Image>();

        completionImage.sprite = _completedSprite;
        completionImage.color = Color.white;
        completionImage.preserveAspect = true;
        completionImage.raycastTarget = false;

        completionMark.SetActive(false);

        // Fallback check mark if no sprite is assigned.
        TMP_Text fallbackCheck = CreateText(
            "FallbackCheck",
            checkObject.transform,
            new Vector2(0f, 0f),
            new Vector2(1f, 1f),
            15f,
            FontStyles.Bold,
            CompletedColor,
            TextAlignmentOptions.Center);

        fallbackCheck.text = "✓";
        fallbackCheck.gameObject.SetActive(false);

        // Task description.
        TMP_Text label = CreateText(
            "TaskText",
            rowObject.transform,
            new Vector2(0.17f, 0f),
            new Vector2(0.755f, 1f),
            14f,
            FontStyles.Normal,
            TextColor,
            TextAlignmentOptions.MidlineLeft);

        // Progress counter for tasks such as window repair.
        TMP_Text progress = CreateText(
            "TaskProgress",
            rowObject.transform,
            new Vector2(0.765f, 0f),
            new Vector2(0.985f, 1f),
            12f,
            FontStyles.Bold,
            MutedTextColor,
            TextAlignmentOptions.MidlineRight);

        // Thin separator line.
        GameObject separator = CreateImageObject(
            "RowSeparator",
            rowObject.transform,
            new Vector2(0.015f, 0f),
            new Vector2(0.985f, 0f),
            SeparatorColor);

        RectTransform separatorRect =
            separator.GetComponent<RectTransform>();

        separatorRect.pivot = new Vector2(0.5f, 0f);
        separatorRect.sizeDelta = new Vector2(0f, 1f);
        separatorRect.anchoredPosition = Vector2.zero;

        return new TaskRow
        {
            Root = rowObject,
            Number = number,
            Label = label,
            Progress = progress,
            CheckBackground = checkBackground,
            CheckOutline = checkOutline,
            CompletionImage = completionImage,
            FallbackCheck = fallbackCheck
        };
    }

    // --------------------------------------------------
    // UI HELPERS
    // --------------------------------------------------

    private TMP_Text CreateText(
        string objectName,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        float fontSize,
        FontStyles style,
        Color color,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = CreateUIObject(
            objectName,
            parent,
            typeof(TextMeshProUGUI));

        RectTransform rect =
            textObject.GetComponent<RectTransform>();

        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        TMP_Text text =
            textObject.GetComponent<TextMeshProUGUI>();

        if (_fontAsset != null)
            text.font = _fontAsset;

        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;

        return text;
    }

    private static GameObject CreateImageObject(
        string objectName,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Color color)
    {
        GameObject imageObject = CreateUIObject(
            objectName,
            parent,
            typeof(Image));

        RectTransform rect =
            imageObject.GetComponent<RectTransform>();

        SetAnchors(rect, anchorMin, anchorMax);

        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;

        return imageObject;
    }

    private static void SetAnchors(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static GameObject CreateUIObject(
        string objectName,
        Transform parent,
        Type componentType)
    {
        GameObject instance = new GameObject(
            objectName,
            typeof(RectTransform),
            componentType);

        instance.transform.SetParent(parent, false);

        return instance;
    }
}