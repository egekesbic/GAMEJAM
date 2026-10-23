using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

public class SettingsMenu : MonoBehaviour
{
    public static SettingsMenu Instance { get; private set; }

    // Other scripts must read this value to apply mouse sensitivity.
    public static float MouseSensitivity =>
        Instance != null
            ? Instance._mouseSensitivity
            : Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, 1f), 0.1f, 3f);

    private const string SensitivityKey = "JamSettings_MouseSensitivity";
    private const string VolumeKey = "JamSettings_MasterVolume";
    private const string BrightnessKey = "JamSettings_Brightness";
    private const string VSyncKey = "JamSettings_VSync";
    private const string DisplayModeKey = "JamSettings_DisplayMode";

    private static readonly FullScreenMode[] DisplayModes =
    {
        FullScreenMode.FullScreenWindow,
        FullScreenMode.Windowed,
        FullScreenMode.ExclusiveFullScreen
    };

    private static readonly string[] DisplayModeLabels =
    {
        "BORDERLESS",
        "WINDOWED",
        "FULLSCREEN"
    };

    // Muted, worn-down colors intended for a horror game.
    private static readonly Color BackgroundColor = Hex("#101212");
    private static readonly Color RowColor = Hex("#191C1C");
    private static readonly Color BorderColor = Hex("#343838");
    private static readonly Color AccentColor = Hex("#873F3F");
    private static readonly Color MainTextColor = Hex("#D8D5CC");
    private static readonly Color MutedTextColor = Hex("#898B84");
    private static readonly Color OnColor = Hex("#A6B19E");
    private static readonly Color OffColor = Hex("#3C2929");

    private float _mouseSensitivity = 1f;
    private float _masterVolume = 0.8f;
    private float _brightness = 1f;

    private bool _vSyncEnabled = true;
    private int _displayModeIndex;
    private bool _isOpen;

    private CursorLockMode _previousCursorLockState;
    private bool _previousCursorVisible;

    private EventSystem _eventSystem;
    private Transform _menuRoot;
    private Image _brightnessImage;

    private Slider _sensitivitySlider;
    private Slider _volumeSlider;
    private Slider _brightnessSlider;

    private TMP_Text _sensitivityValue;
    private TMP_Text _volumeValue;
    private TMP_Text _brightnessValue;
    private TMP_Text _displayModeValue;
    private TMP_Text _vSyncButtonText;

    private Button _vSyncButton;
    private Coroutine _saveCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (transform.parent != null)
            transform.SetParent(null);

        DontDestroyOnLoad(gameObject);

        LoadSettings();
        EnsureEventSystem();

        SceneManager.sceneLoaded += HandleSceneLoaded;

        BuildBrightnessOverlay();
        BuildMenu();
        ApplyAllSettings();

        SetMenuOpen(false, false);
    }

    private void Update()
    {
        if (EscapePressed())
            SetMenuOpen(!_isOpen);
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            SaveSettings();
    }

    private void OnApplicationQuit()
    {
        SaveSettings();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;

        if (_saveCoroutine != null)
            StopCoroutine(_saveCoroutine);

        if (Instance == this)
            Instance = null;
    }

    // INPUT

    private bool EscapePressed()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        return Keyboard.current != null &&
               Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    // EVENT SYSTEM

    private void EnsureEventSystem()
    {
        _eventSystem = FindFirstObjectByType<EventSystem>();

        if (_eventSystem == null)
        {
            GameObject eventSystemObject = new GameObject(
                "Persistent EventSystem",
                typeof(EventSystem));

            eventSystemObject.transform.SetParent(transform, false);
            _eventSystem = eventSystemObject.GetComponent<EventSystem>();
        }
        else if (_eventSystem.transform != transform &&
                 _eventSystem.transform.parent != transform)
        {
            _eventSystem.transform.SetParent(transform, false);
        }

        EnsureCorrectInputModule();
    }

    private void EnsureCorrectInputModule()
    {
        BaseInputModule[] modules =
            _eventSystem.GetComponents<BaseInputModule>();

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        InputSystemUIInputModule correctModule =
            _eventSystem.GetComponent<InputSystemUIInputModule>();
#else
        StandaloneInputModule correctModule =
            _eventSystem.GetComponent<StandaloneInputModule>();
#endif

        if (correctModule == null)
        {
            foreach (BaseInputModule module in modules)
                module.enabled = false;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            correctModule =
                _eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
#else
            correctModule =
                _eventSystem.gameObject.AddComponent<StandaloneInputModule>();
#endif
        }
        else
        {
            foreach (BaseInputModule module in modules)
            {
                if (module != correctModule)
                    module.enabled = false;
            }
        }

        correctModule.enabled = true;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EventSystem[] systems =
            FindObjectsByType<EventSystem>(FindObjectsSortMode.None);

        foreach (EventSystem system in systems)
        {
            if (system == _eventSystem)
                continue;

            system.enabled = false;

            BaseInputModule[] modules =
                system.GetComponents<BaseInputModule>();

            foreach (BaseInputModule module in modules)
                module.enabled = false;
        }
    }

    // SETTINGS / SAVE

    private void LoadSettings()
    {
        _mouseSensitivity = Mathf.Clamp(
            PlayerPrefs.GetFloat(SensitivityKey, 1f),
            0.1f,
            3f);

        _masterVolume = Mathf.Clamp01(
            PlayerPrefs.GetFloat(VolumeKey, 0.8f));

        _brightness = Mathf.Clamp(
            PlayerPrefs.GetFloat(BrightnessKey, 1f),
            0.6f,
            1.4f);

        // VSync defaults to enabled on the first launch.
        _vSyncEnabled = PlayerPrefs.GetInt(VSyncKey, 1) == 1;

        _displayModeIndex = Mathf.Clamp(
            PlayerPrefs.GetInt(DisplayModeKey, 0),
            0,
            DisplayModes.Length - 1);
    }

    private void SaveSettings()
    {
        if (_saveCoroutine != null)
        {
            StopCoroutine(_saveCoroutine);
            _saveCoroutine = null;
        }

        PlayerPrefs.SetFloat(SensitivityKey, _mouseSensitivity);
        PlayerPrefs.SetFloat(VolumeKey, _masterVolume);
        PlayerPrefs.SetFloat(BrightnessKey, _brightness);
        PlayerPrefs.SetInt(VSyncKey, _vSyncEnabled ? 1 : 0);
        PlayerPrefs.SetInt(DisplayModeKey, _displayModeIndex);

        PlayerPrefs.Save();
    }

    private void QueueSave()
    {
        if (_saveCoroutine != null)
            StopCoroutine(_saveCoroutine);

        _saveCoroutine = StartCoroutine(SaveAfterDelay());
    }

    private IEnumerator SaveAfterDelay()
    {
        yield return new WaitForSecondsRealtime(0.6f);

        _saveCoroutine = null;
        SaveSettings();
    }

    private void ApplyAllSettings()
    {
        AudioListener.volume = _masterVolume;
        ApplyVSyncSettings();
        Screen.fullScreenMode = DisplayModes[_displayModeIndex];

        ApplyBrightness();

        if (_sensitivitySlider != null)
            _sensitivitySlider.SetValueWithoutNotify(_mouseSensitivity);

        if (_volumeSlider != null)
            _volumeSlider.SetValueWithoutNotify(_masterVolume);

        if (_brightnessSlider != null)
            _brightnessSlider.SetValueWithoutNotify(_brightness);

        RefreshValueLabels();
        RefreshDisplayModeLabel();
        RefreshVSyncButton();
    }

    private void ApplyBrightness()
    {
        if (_brightnessImage == null)
            return;

        if (_brightness < 1f)
        {
            _brightnessImage.color = new Color(
                0f,
                0f,
                0f,
                1f - _brightness);
        }
        else
        {
            _brightnessImage.color = new Color(
                1f,
                1f,
                1f,
                (_brightness - 1f) * 0.55f);
        }
    }

    // MENU OPEN / CLOSE

    private void SetMenuOpen(bool open, bool restoreCursor = true)
    {
        if (_menuRoot == null)
            return;

        if (_isOpen == open &&
            _menuRoot.gameObject.activeSelf == open)
            return;

        bool wasOpen = _isOpen;

        if (open)
        {
            _previousCursorLockState = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;

            _menuRoot.gameObject.SetActive(true);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _isOpen = true;
        }
        else
        {
            _menuRoot.gameObject.SetActive(false);
            _isOpen = false;

            if (restoreCursor && wasOpen)
            {
                Cursor.lockState = _previousCursorLockState;
                Cursor.visible = _previousCursorVisible;
            }

            if (wasOpen)
                SaveSettings();
        }
    }

    private void CloseMenu()
    {
        SetMenuOpen(false);
    }

    // SLIDER EVENTS

    private void OnSensitivityChanged(float value)
    {
        _mouseSensitivity = value;

        if (_sensitivityValue != null)
            _sensitivityValue.text = $"{value:0.00}";

        QueueSave();
    }

    private void OnVolumeChanged(float value)
    {
        _masterVolume = value;
        AudioListener.volume = value;

        if (_volumeValue != null)
            _volumeValue.text = $"{Mathf.RoundToInt(value * 100f)}%";

        QueueSave();
    }

    private void OnBrightnessChanged(float value)
    {
        _brightness = value;
        ApplyBrightness();

        if (_brightnessValue != null)
            _brightnessValue.text = $"{Mathf.RoundToInt(value * 100f)}%";

        QueueSave();
    }

    // DISPLAY / VSYNC

    private void CycleDisplayMode(int direction)
    {
        _displayModeIndex =
            (_displayModeIndex + direction + DisplayModes.Length)
            % DisplayModes.Length;

        Screen.fullScreenMode = DisplayModes[_displayModeIndex];

        RefreshDisplayModeLabel();
        SaveSettings();
    }

    private void ToggleVSync()
    {
        _vSyncEnabled = !_vSyncEnabled;

        ApplyVSyncSettings();
        RefreshVSyncButton();
        SaveSettings();
    }

    private void RefreshDisplayModeLabel()
    {
        if (_displayModeValue != null)
            _displayModeValue.text = DisplayModeLabels[_displayModeIndex];
    }

    private void RefreshVSyncButton()
    {
        if (_vSyncButton == null || _vSyncButtonText == null)
            return;

        Image image = _vSyncButton.GetComponent<Image>();

        if (_vSyncEnabled)
        {
            image.color = OnColor;
            _vSyncButtonText.color = Hex("#151915");
            _vSyncButtonText.text = "ON";
        }
        else
        {
            image.color = OffColor;
            _vSyncButtonText.color = MainTextColor;
            _vSyncButtonText.text = "OFF";
        }
    }

    private void RefreshValueLabels()
    {
        if (_sensitivityValue != null)
            _sensitivityValue.text = $"{_mouseSensitivity:0.00}";

        if (_volumeValue != null)
            _volumeValue.text =
                $"{Mathf.RoundToInt(_masterVolume * 100f)}%";

        if (_brightnessValue != null)
            _brightnessValue.text =
                $"{Mathf.RoundToInt(_brightness * 100f)}%";
    }

    private void ResetDefaults()
    {
        _mouseSensitivity = 1f;
        _masterVolume = 0.8f;
        _brightness = 1f;
        _vSyncEnabled = true;
        _displayModeIndex = 0;

        ApplyAllSettings();
        SaveSettings();
    }

    // UI GENERATION

    private void BuildBrightnessOverlay()
    {
        GameObject canvasObject = new GameObject(
            "Brightness Overlay",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler));

        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // Keep it above normal game HUD canvases but below the settings menu.
        canvas.sortingOrder = 32000;

        SetupCanvasScaler(canvasObject.GetComponent<CanvasScaler>());

        _brightnessImage = CreateImage(
            canvasObject.transform,
            "Brightness Tint",
            new Color(0f, 0f, 0f, 0f),
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero,
            false);
    }

    private void BuildMenu()
    {
        GameObject canvasObject = new GameObject(
            "Settings Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));

        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32001;

        SetupCanvasScaler(canvasObject.GetComponent<CanvasScaler>());

        GameObject rootObject =
            new GameObject("Settings Layer", typeof(RectTransform));

        rootObject.transform.SetParent(canvasObject.transform, false);
        _menuRoot = rootObject.transform;

        CreateImage(
            _menuRoot,
            "Backdrop",
            new Color(0.008f, 0.01f, 0.01f, 0.84f),
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero,
            true);

        Image panelImage = CreateImage(
            _menuRoot,
            "Settings Panel",
            BackgroundColor,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero,
            false);

        RectTransform panel = panelImage.rectTransform;
        panel.sizeDelta = new Vector2(600f, 560f);

        // A single worn red line; no decorative terminal branding.
        CreateImage(
            panel,
            "Top Line",
            AccentColor,
            new Vector2(0f, 0.99f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero,
            false);

        CreateText(
            panel,
            "Title",
            "SETTINGS",
            30,
            MainTextColor,
            FontStyles.Bold,
            new Vector2(0.07f, 0.84f),
            new Vector2(0.93f, 0.95f),
            TextAlignmentOptions.Left);

        CreateImage(
            panel,
            "Header Line",
            BorderColor,
            new Vector2(0.07f, 0.815f),
            new Vector2(0.93f, 0.818f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero,
            false);

        _sensitivitySlider = CreateSliderRow(
            panel,
            "Mouse sensitivity",
            new Vector2(0.07f, 0.675f),
            new Vector2(0.93f, 0.785f),
            0.1f,
            3f,
            _mouseSensitivity,
            out _sensitivityValue,
            OnSensitivityChanged);

        _volumeSlider = CreateSliderRow(
            panel,
            "Master volume",
            new Vector2(0.07f, 0.535f),
            new Vector2(0.93f, 0.645f),
            0f,
            1f,
            _masterVolume,
            out _volumeValue,
            OnVolumeChanged);

        _brightnessSlider = CreateSliderRow(
            panel,
            "Brightness",
            new Vector2(0.07f, 0.395f),
            new Vector2(0.93f, 0.505f),
            0.6f,
            1.4f,
            _brightness,
            out _brightnessValue,
            OnBrightnessChanged);

        // Display mode
        CreateImage(
            panel,
            "Display Row",
            RowColor,
            new Vector2(0.07f, 0.255f),
            new Vector2(0.93f, 0.365f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero,
            false);

        CreateText(
            panel,
            "Display Label",
            "Display mode",
            14,
            MainTextColor,
            FontStyles.Normal,
            new Vector2(0.10f, 0.285f),
            new Vector2(0.48f, 0.335f),
            TextAlignmentOptions.Left);

        CreateButton(
            panel,
            "Previous Mode",
            "<",
            new Vector2(0.55f, 0.275f),
            new Vector2(0.63f, 0.345f),
            BorderColor,
            MainTextColor,
            () => CycleDisplayMode(-1),
            17);

        _displayModeValue = CreateText(
            panel,
            "Display Mode Value",
            "",
            10,
            MainTextColor,
            FontStyles.Bold,
            new Vector2(0.635f, 0.275f),
            new Vector2(0.85f, 0.345f),
            TextAlignmentOptions.Center);

        CreateButton(
            panel,
            "Next Mode",
            ">",
            new Vector2(0.86f, 0.275f),
            new Vector2(0.93f, 0.345f),
            BorderColor,
            MainTextColor,
            () => CycleDisplayMode(1),
            17);

        // VSync
        CreateText(
            panel,
            "VSync Label",
            "Vertical sync",
            14,
            MainTextColor,
            FontStyles.Normal,
            new Vector2(0.09f, 0.165f),
            new Vector2(0.55f, 0.225f),
            TextAlignmentOptions.Left);

        _vSyncButton = CreateButton(
            panel,
            "VSync Toggle",
            "",
            new Vector2(0.77f, 0.165f),
            new Vector2(0.91f, 0.225f),
            OnColor,
            Hex("#151915"),
            ToggleVSync,
            11);

        _vSyncButtonText =
            _vSyncButton.GetComponentInChildren<TMP_Text>();

        CreateImage(
            panel,
            "Footer Line",
            BorderColor,
            new Vector2(0.07f, 0.125f),
            new Vector2(0.93f, 0.128f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero,
            false);

        CreateButton(
            panel,
            "Reset Button",
            "RESET",
            new Vector2(0.07f, 0.045f),
            new Vector2(0.28f, 0.105f),
            BorderColor,
            MainTextColor,
            ResetDefaults,
            10);

        CreateText(
            panel,
            "Escape Hint",
            "ESC  —  BACK TO GAME",
            9,
            MutedTextColor,
            FontStyles.Normal,
            new Vector2(0.31f, 0.045f),
            new Vector2(0.69f, 0.105f),
            TextAlignmentOptions.Center);

        CreateButton(
            panel,
            "Close Button",
            "RESUME",
            new Vector2(0.72f, 0.045f),
            new Vector2(0.93f, 0.105f),
            AccentColor,
            MainTextColor,
            CloseMenu,
            10);

        RefreshValueLabels();
        RefreshDisplayModeLabel();
        RefreshVSyncButton();
    }

    private Slider CreateSliderRow(
        Transform parent,
        string title,
        Vector2 anchorMin,
        Vector2 anchorMax,
        float minValue,
        float maxValue,
        float initialValue,
        out TMP_Text valueLabel,
        UnityAction<float> callback)
    {
        Image row = CreateImage(
            parent,
            title + " Row",
            RowColor,
            anchorMin,
            anchorMax,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero,
            false);

        Transform rowTransform = row.transform;

        CreateText(
            rowTransform,
            title + " Label",
            title,
            13,
            MainTextColor,
            FontStyles.Normal,
            new Vector2(0.035f, 0.57f),
            new Vector2(0.70f, 0.88f),
            TextAlignmentOptions.Left);

        valueLabel = CreateText(
            rowTransform,
            title + " Value",
            "",
            12,
            MainTextColor,
            FontStyles.Bold,
            new Vector2(0.73f, 0.57f),
            new Vector2(0.965f, 0.88f),
            TextAlignmentOptions.Right);

        GameObject sliderObject = new GameObject(
            title + " Slider",
            typeof(RectTransform),
            typeof(Slider));

        sliderObject.transform.SetParent(rowTransform, false);

        RectTransform sliderRect =
            sliderObject.GetComponent<RectTransform>();

        sliderRect.anchorMin = new Vector2(0.035f, 0.14f);
        sliderRect.anchorMax = new Vector2(0.965f, 0.33f);
        sliderRect.offsetMin = Vector2.zero;
        sliderRect.offsetMax = Vector2.zero;

        // Important: the track must receive raycasts.
        // Otherwise only clicking the small handle reliably works.
        CreateImage(
            sliderRect,
            "Track",
            Hex("#080A0A"),
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero,
            true);

        RectTransform fillArea = CreateRect(
            sliderRect,
            "Fill Area",
            new Vector2(0f, 0.15f),
            new Vector2(1f, 0.85f));

        Image fill = CreateImage(
            fillArea,
            "Fill",
            AccentColor,
            Vector2.zero,
            new Vector2(0.5f, 1f),
            new Vector2(0f, 0.5f),
            Vector2.zero,
            Vector2.zero,
            false);

        RectTransform handleArea = CreateRect(
            sliderRect,
            "Handle Slide Area",
            Vector2.zero,
            Vector2.one);

        Image handleImage = CreateImage(
            handleArea,
            "Handle",
            MainTextColor,
            Vector2.zero,
            new Vector2(0f, 1f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(10f, 0f),
            true);

        Slider slider = sliderObject.GetComponent<Slider>();

        slider.minValue = minValue;
        slider.maxValue = maxValue;
        slider.wholeNumbers = false;
        slider.direction = Slider.Direction.LeftToRight;
        slider.targetGraphic = handleImage;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handleImage.rectTransform;
        slider.interactable = true;

        slider.SetValueWithoutNotify(
            Mathf.Clamp(initialValue, minValue, maxValue));

        slider.onValueChanged.AddListener(callback);

        return slider;
    }

    private Button CreateButton(
        Transform parent,
        string objectName,
        string label,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Color backgroundColor,
        Color textColor,
        UnityAction action,
        int fontSize)
    {
        Image image = CreateImage(
            parent,
            objectName,
            backgroundColor,
            anchorMin,
            anchorMax,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero,
            true);

        Button button = image.gameObject.AddComponent<Button>();

        ColorBlock colors = button.colors;
        colors.normalColor = backgroundColor;
        colors.highlightedColor =
            Color.Lerp(backgroundColor, Color.white, 0.10f);
        colors.pressedColor =
            Color.Lerp(backgroundColor, Color.black, 0.20f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = backgroundColor * 0.7f;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;

        button.colors = colors;
        button.targetGraphic = image;
        button.onClick.AddListener(action);

        CreateText(
            image.transform,
            "Label",
            label,
            fontSize,
            textColor,
            FontStyles.Bold,
            Vector2.zero,
            Vector2.one,
            TextAlignmentOptions.Center);

        return button;
    }

    private TMP_Text CreateText(
        Transform parent,
        string objectName,
        string content,
        int fontSize,
        Color color,
        FontStyles style,
        Vector2 anchorMin,
        Vector2 anchorMax,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(TextMeshProUGUI));

        textObject.transform.SetParent(parent, false);

        RectTransform rect =
            textObject.GetComponent<RectTransform>();

        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        TMP_Text text = textObject.GetComponent<TMP_Text>();

        text.text = content;
        text.fontSize = fontSize;
        text.color = color;
        text.fontStyle = style;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;

        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        return text;
    }

    private Image CreateImage(
        Transform parent,
        string objectName,
        Color color,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 offsetMin,
        Vector2 offsetMax,
        bool raycastTarget)
    {
        GameObject imageObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(Image));

        imageObject.transform.SetParent(parent, false);

        RectTransform rect =
            imageObject.GetComponent<RectTransform>();

        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = raycastTarget;

        return image;
    }

    private RectTransform CreateRect(
        Transform parent,
        string objectName,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        GameObject obj = new GameObject(
            objectName,
            typeof(RectTransform));

        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        return rect;
    }

    private void SetupCanvasScaler(CanvasScaler scaler)
    {
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode =
            CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color color);
        return color;
    }

    private void ApplyVSyncSettings()
    {
        QualitySettings.vSyncCount = _vSyncEnabled ? 1 : 0;

        // VSync kapalıyken başka bir FPS sınırı bırakma.
        Application.targetFrameRate = -1;

        Debug.Log(
            $"VSync: {QualitySettings.vSyncCount}, " +
            $"Target FPS: {Application.targetFrameRate}");
    }
}