using UnityEngine;
using UnityEngine.UI;

/// <summary>Attach to each small fuse UI Button and to the large main-switch UI Button.</summary>
public class FuseButton : MonoBehaviour, IPlayerInteractable
{
    private int _index;
    private bool _isMainSwitch;
    [SerializeField] private string _prompt = "F - Sigortayı değiştir";

    private FuseBox _fuseBox;
    private Button _button;

    public FuseBox Box => _fuseBox != null ? _fuseBox : FuseBox.SharedInstance;
    public Button UiButton
    {
        get
        {
            if (_button == null)
                _button = GetComponent<Button>();
            return _button;
        }
    }
    public int Index => _index;
    public bool IsMainSwitch => _isMainSwitch;
    public bool IsWorldInteraction => UiButton == null;
    public bool CanInteract
    {
        get
        {
            FuseBox fuseBox = Box;
            if (fuseBox == null)
                return false;

            return IsWorldInteraction
                ? fuseBox.CanOpenPanel
                : (_isMainSwitch ? fuseBox.CanTurnOffMainSwitch : fuseBox.CanInteractWithFuse(_index));
        }
    }
    public string InteractionPrompt
    {
        get
        {
            FuseBox fuseBox = Box;
            if (fuseBox == null)
                return string.Empty;

            if (IsWorldInteraction)
                return fuseBox.CanOpenPanel ? fuseBox.OpenPrompt : string.Empty;

            return CanInteract ? _prompt : string.Empty;
        }
    }

    private void Awake()
    {
        _button = GetComponent<Button>();
        if (_button == null)
            EnsureWorldInteractionCollider();
    }

    private void EnsureWorldInteractionCollider()
    {
        if (GetComponent<Collider>() != null)
            return;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        Bounds worldBounds = default;
        bool hasBounds = false;
        foreach (Renderer targetRenderer in renderers)
        {
            if (targetRenderer == null)
                continue;

            if (!hasBounds)
            {
                worldBounds = targetRenderer.bounds;
                hasBounds = true;
            }
            else
            {
                worldBounds.Encapsulate(targetRenderer.bounds);
            }
        }

        BoxCollider interactionCollider = gameObject.AddComponent<BoxCollider>();
        interactionCollider.isTrigger = true;

        Vector3 localCenter = Vector3.zero;
        Vector3 localSize = Vector3.one;
        if (hasBounds && worldBounds.size.sqrMagnitude > 0.0001f)
        {
            Vector3 localMin = Vector3.positiveInfinity;
            Vector3 localMax = Vector3.negativeInfinity;
            for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
            {
                Vector3 signs = new Vector3(
                    (cornerIndex & 1) == 0 ? -1f : 1f,
                    (cornerIndex & 2) == 0 ? -1f : 1f,
                    (cornerIndex & 4) == 0 ? -1f : 1f);
                Vector3 worldCorner = worldBounds.center + Vector3.Scale(worldBounds.extents, signs);
                Vector3 localCorner = transform.InverseTransformPoint(worldCorner);
                localMin = Vector3.Min(localMin, localCorner);
                localMax = Vector3.Max(localMax, localCorner);
            }

            localCenter = (localMin + localMax) * 0.5f;
            localSize = localMax - localMin;
        }

        Vector3 lossyScale = transform.lossyScale;
        Vector3 minimumLocalSize = new Vector3(
            0.7f / Mathf.Max(Mathf.Abs(lossyScale.x), 0.001f),
            1.0f / Mathf.Max(Mathf.Abs(lossyScale.y), 0.001f),
            0.5f / Mathf.Max(Mathf.Abs(lossyScale.z), 0.001f));
        interactionCollider.center = localCenter;
        interactionCollider.size = Vector3.Max(localSize, minimumLocalSize);
    }

    /// <summary>The local FuseBox assigns this button's network target and index after spawn.</summary>
    public void Initialize(FuseBox fuseBox, int index, bool isMainSwitch)
    {
        _fuseBox = fuseBox;
        _index = index;
        _isMainSwitch = isMainSwitch;
        if (_button == null)
            _button = GetComponent<Button>();
    }

    public void Interact()
    {
        FuseBox fuseBox = Box;
        if (fuseBox == null)
            return;

        if (IsWorldInteraction)
        {
            fuseBox.OpenPanel();
            return;
        }

        if (!CanInteract)
            return;

        if (_isMainSwitch)
            fuseBox.RequestMainSwitchOff();
        else
            fuseBox.RequestFuseInteraction(_index);
    }

    public void Interact(Vector3 rayOrigin, Vector3 rayDirection)
    {
        Interact();
    }

    public void RefreshFuse(bool isOn)
    {
        FuseBox fuseBox = Box;
        if (_isMainSwitch || fuseBox == null || _button == null)
            return;

        if (_button.image != null)
            _button.image.sprite = isOn ? fuseBox.OnSprite : fuseBox.OffSprite;

        _button.interactable = CanInteract;
    }

    public void RefreshMainSwitch(bool canTurnOff, bool isOff)
    {
        FuseBox fuseBox = Box;
        if (!_isMainSwitch || fuseBox == null || _button == null)
            return;

        if (_button.image != null)
            _button.image.sprite = isOff
                ? fuseBox.MainSwitchOffSprite
                : fuseBox.MainSwitchOnSprite;

        _button.interactable = canTurnOff;
    }
}
