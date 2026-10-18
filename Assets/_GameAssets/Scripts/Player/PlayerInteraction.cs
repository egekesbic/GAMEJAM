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
            SetPrompt(string.Empty);
            if (Input.GetKeyDown(KeyCode.Escape))
                CloseFuseBox();
            return;
        }

        currentTarget = FindTargetInView();
        SetPrompt(currentTarget != null ? currentTarget.InteractionPrompt : string.Empty);

        if (currentTarget == null || !Input.GetKeyDown(KeyCode.F))
            return;

        if (currentTarget is FuseBox targetFuseBox)
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

    private void ResolveReferences()
    {
        if (ownerCamera == null)
            ownerCamera = GetComponentInChildren<Camera>(true);

        if (promptLabel == null)
            promptLabel = GetComponentInChildren<TMP_Text>(true);

        if (promptLabel != null)
        {
            promptCanvas = promptLabel.GetComponentInParent<Canvas>(true);
        }
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
