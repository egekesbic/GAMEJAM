using Unity.Netcode;
using UnityEngine;

public sealed class WaterValveInteractable : MonoBehaviour, IPlayerInteractable
{
    [SerializeField] private string _interactionPrompt = "F: Vanayla etkileşime geç";

    public NetworkObject NetworkObject { get; private set; }
    public string InteractionPrompt => _interactionPrompt;

    private void Awake()
    {
        NetworkObject = GetComponentInParent<NetworkObject>();
        EnsureInteractionCollider();
    }

    public void Interact(Vector3 rayOrigin, Vector3 rayDirection)
    {
        // PlayerInteraction forwards F to the WaterValve component on the local player.
    }

    private void EnsureInteractionCollider()
    {
        if (GetComponent<Collider>() != null || GetComponentInChildren<Collider>(true) != null)
            return;

        BoxCollider interactionCollider = gameObject.AddComponent<BoxCollider>();
        interactionCollider.isTrigger = true;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            interactionCollider.size = new Vector3(0.7f, 1f, 0.5f);
            return;
        }

        Bounds worldBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            worldBounds.Encapsulate(renderers[i].bounds);

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

        Vector3 lossyScale = transform.lossyScale;
        Vector3 minimumLocalSize = new Vector3(
            0.7f / Mathf.Max(Mathf.Abs(lossyScale.x), 0.001f),
            1f / Mathf.Max(Mathf.Abs(lossyScale.y), 0.001f),
            0.5f / Mathf.Max(Mathf.Abs(lossyScale.z), 0.001f));

        interactionCollider.center = (localMin + localMax) * 0.5f;
        interactionCollider.size = Vector3.Max(localMax - localMin, minimumLocalSize);
    }
}
