using Unity.Netcode;
using UnityEngine;
using TMPro;

public sealed class PlayerInteraction : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Camera ownerCamera;
    [SerializeField] private TMP_Text promptLabel;
    [Header("Interaction")]
    [SerializeField, Min(0.1f)] private float interactDistance = 3f;
    [SerializeField] private LayerMask interactionMask = ~0;

    private readonly RaycastHit[] raycastHits = new RaycastHit[32];
    private IPlayerInteractable currentTarget;
    private FuseBox openedFuseBox;
    private Canvas promptCanvas;
    private WaterValve waterValve;

    public override void OnNetworkSpawn()
    {
        ResolveReferences();

        if (!IsOwner)
        {
            if (ownerCamera != null)
                ownerCamera.gameObject.SetActive(false);

            if (promptCanvas != null)
                promptCanvas.gameObject.SetActive(false);
            SetPrompt(string.Empty);
            enabled = false;
            return;
        }

        if (ownerCamera != null)
            ownerCamera.gameObject.SetActive(true);
        else
            Debug.LogError("PlayerInteraction: Owner Camera is missing. Assign it or add a Camera under the player.", this);

        if (promptCanvas != null)
        {
            promptCanvas.gameObject.SetActive(true);
            promptCanvas.enabled = true;
            promptCanvas.transform.localScale = Vector3.one;
            promptCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            promptCanvas.worldCamera = null;
            promptCanvas.overrideSorting = true;
            promptCanvas.sortingOrder = Mathf.Max(50, promptCanvas.sortingOrder);
        }

        if (promptLabel == null)
            Debug.LogError("PlayerInteraction: Prompt TMP_Text is missing. Assign Prompt Label or add it under the player.", this);

        SetPrompt(string.Empty);
        enabled = true;
    }

    public override void OnNetworkDespawn()
    {
        SetCurrentTarget(null);

        if (openedFuseBox != null)
            openedFuseBox.ClosePanel();

        SetPrompt(string.Empty);
    }

    private void Update()
    {
        if (!IsOwner || ownerCamera == null)
            return;

        if (openedFuseBox != null && openedFuseBox.IsPanelOpen)
        {
            SetCurrentTarget(null);
            SetPrompt(string.Empty);
            if (Input.GetKeyDown(KeyCode.Escape))
                CloseFuseBox();
            return;
        }

        SetCurrentTarget(FindTargetInView());
        string interactionPrompt = currentTarget != null ? currentTarget.InteractionPrompt : string.Empty;
        if (currentTarget is WaterValveInteractable lookedAtValve && waterValve != null)
            interactionPrompt = waterValve.GetInteractionPrompt(lookedAtValve);
        SetPrompt(interactionPrompt);

        if (currentTarget is IPlayerLeftClickInteractable leftClickTarget)
        {
            if (Input.GetMouseButtonDown(0))
                leftClickTarget.InteractLeftClick();

            return;
        }

        if (currentTarget == null || !Input.GetKeyDown(KeyCode.F))
            return;

        if (currentTarget is WaterValveInteractable targetValve)
        {
            if (waterValve != null)
                waterValve.BeginInteraction(targetValve);
            else
                Debug.LogError("PlayerInteraction: Add WaterValve to the player object to handle valve interaction.", this);
        }
        else if (currentTarget is FuseBox targetFuseBox)
        {
            openedFuseBox = targetFuseBox;
            openedFuseBox.OpenPanel();
        }
        else if (currentTarget is FuseButton targetFuseButton && targetFuseButton.IsWorldInteraction)
        {
            openedFuseBox = targetFuseButton.Box;
            if (openedFuseBox != null)
                openedFuseBox.OpenPanel();
        }
        else
        {
            currentTarget.Interact(ownerCamera.transform.position, ownerCamera.transform.forward);
        }
    }

    private void SetCurrentTarget(IPlayerInteractable nextTarget)
    {
        if (ReferenceEquals(currentTarget, nextTarget))
            return;

        if (currentTarget is IPlayerLeftClickInteractable previousLeftClickTarget)
            previousLeftClickTarget.SetInteractionFocused(false);

        currentTarget = nextTarget;

        if (currentTarget is IPlayerLeftClickInteractable nextLeftClickTarget)
            nextLeftClickTarget.SetInteractionFocused(true);

        if (waterValve != null)
            waterValve.OnLookTargetChanged(currentTarget as WaterValveInteractable);


    }

    private void ResolveReferences()
    {
        if (ownerCamera == null)
            ownerCamera = GetComponentInChildren<Camera>(true);

        if (promptLabel == null)
            promptLabel = GetComponentInChildren<TMP_Text>(true);

        if (promptLabel != null)
            promptCanvas = promptLabel.GetComponentInParent<Canvas>(true);

        if (waterValve == null)
            waterValve = GetComponent<WaterValve>();
    }

    private IPlayerInteractable FindTargetInView()
    {
        Ray ray = new Ray(ownerCamera.transform.position, ownerCamera.transform.forward);
        int hitCount = Physics.RaycastNonAlloc(
            ray,
            raycastHits,
            interactDistance,
            interactionMask,
            QueryTriggerInteraction.Collide);

        IPlayerInteractable nearestTarget = null;
        float nearestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = raycastHits[i];
            if (hit.collider == null || hit.distance >= nearestDistance)
                continue;

            IPlayerInteractable candidate = hit.collider.GetComponentInParent<IPlayerInteractable>();
            if (candidate == null)
                continue;

            nearestTarget = candidate;
            nearestDistance = hit.distance;
        }

        return nearestTarget;
    }

    private void SetPrompt(string text)
    {
        bool show = !string.IsNullOrWhiteSpace(text);

        if (promptLabel != null)
        {
            if (promptLabel.text != text)
                promptLabel.text = text;
            promptLabel.enabled = true;
            promptLabel.raycastTarget = false;
        }

        if (promptLabel != null && promptLabel.gameObject.activeSelf != show)
            promptLabel.gameObject.SetActive(show);
    }

    private void CloseFuseBox()
    {
        if (openedFuseBox != null)
            openedFuseBox.ClosePanel();

        openedFuseBox = null;
    }
}

public interface IPlayerInteractable
{
    string InteractionPrompt { get; }
    void Interact(Vector3 rayOrigin, Vector3 rayDirection);
}

public interface IPlayerLeftClickInteractable : IPlayerInteractable
{
    void InteractLeftClick();
    void SetInteractionFocused(bool isFocused);
}
