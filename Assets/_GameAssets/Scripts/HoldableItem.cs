using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(NetworkObject), typeof(NetworkTransform), typeof(Rigidbody))]
public sealed class HoldableItem : NetworkBehaviour
{
    [Header("Hold")]
    [SerializeField] private bool twoHanded;
    [SerializeField] private bool canBePickedUp = true;

    private Transform visualRoot;

    private readonly NetworkVariable<ulong> holderClientId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Rigidbody body;
    private Collider[] colliders;
    private Vector3 visualStartPosition;
    private Quaternion visualStartRotation;
    private Vector3 visualStartScale;
    private Transform ownerViewParent;
    private Transform originalVisualParent;
    private GameObject ownerViewProxy;
    private MeshFilter rootMeshFilter;
    private MeshRenderer rootMeshRenderer;
    private bool rootRendererWasEnabled;
    private Vector3 ownerViewPosition;
    private Quaternion ownerViewRotation;
    private bool ownerViewAttached;
    private bool warnedNoVisualChild;

    public bool TwoHanded => twoHanded;
    public bool CanBePickedUp => canBePickedUp;
    public bool IsHeld => holderClientId.Value != ulong.MaxValue;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>(true);
        visualRoot = FindVisualRoot();
        if (visualRoot == transform)
        {
            rootMeshFilter = GetComponent<MeshFilter>();
            rootMeshRenderer = GetComponent<MeshRenderer>();
            if (rootMeshRenderer != null)
                rootRendererWasEnabled = rootMeshRenderer.enabled;
        }

        visualStartPosition = visualRoot.localPosition;
        visualStartRotation = visualRoot.localRotation;
        visualStartScale = visualRoot.localScale;
        originalVisualParent = visualRoot.parent;
    }

    private Transform FindVisualRoot()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return transform;

        Transform candidate = renderers[0].transform;
        while (candidate != null && candidate != transform)
        {
            bool containsAllRenderers = true;
            for (int i = 0; i < renderers.Length; i++)
            {
                Transform rendererTransform = renderers[i].transform;
                if (rendererTransform != candidate && !rendererTransform.IsChildOf(candidate))
                {
                    containsAllRenderers = false;
                    break;
                }
            }

            if (containsAllRenderers)
                return candidate;

            candidate = candidate.parent;
        }

        if (renderers[0].transform != transform)
            return renderers[0].transform;

        return transform;
    }

    public override void OnNetworkSpawn()
    {
        holderClientId.OnValueChanged += OnHolderChanged;
        ApplyHeldPhysics(IsHeld);
        base.OnNetworkSpawn();
    }

    public override void OnNetworkDespawn()
    {
        if (ownerViewAttached)
            DetachOwnerView();
        holderClientId.OnValueChanged -= OnHolderChanged;
        base.OnNetworkDespawn();
    }

    public bool IsHeldBy(ulong clientId)
    {
        return IsHeld && holderClientId.Value == clientId;
    }

    public void ServerPickUp(NetworkObject playerObject, Vector3 holdPosition, Quaternion holdRotation)
    {
        if (!IsServer || !IsSpawned || !canBePickedUp || IsHeld || playerObject == null)
            return;

        transform.SetPositionAndRotation(holdPosition, holdRotation);
        holderClientId.Value = playerObject.OwnerClientId;
        ApplyHeldPhysics(true);
    }

    public void ServerSetHeldPose(ulong holderId, Vector3 worldPosition, Quaternion worldRotation)
    {
        if (!IsServer || !IsSpawned || !IsHeldBy(holderId))
            return;

        transform.SetPositionAndRotation(worldPosition, worldRotation);
    }

    public void ServerDrop(Vector3 velocity, Vector3 requestedDropPosition, Quaternion requestedDropRotation)
    {
        if (!IsServer || !IsSpawned || !IsHeld)
            return;

        holderClientId.Value = ulong.MaxValue;

        transform.SetPositionAndRotation(requestedDropPosition, requestedDropRotation);
        ApplyHeldPhysics(false);

        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            if (velocity.sqrMagnitude > 0.001f)
                body.AddForce(velocity, ForceMode.VelocityChange);
        }
    }

    public void AttachOwnerView(Transform viewParent, Vector3 localPosition)
    {
        if (!IsSpawned || viewParent == null || visualRoot == null)
            return;

        if (visualRoot == transform)
        {
            if (rootMeshFilter == null || rootMeshRenderer == null)
            {
                if (!warnedNoVisualChild)
                {
                    Debug.LogWarning($"{name}: Root visual needs a MeshFilter and MeshRenderer to create its local held view.", this);
                    warnedNoVisualChild = true;
                }
                return;
            }

            EnsureOwnerViewProxy();
            if (ownerViewProxy == null)
                return;
        }

        ownerViewAttached = true;
        ownerViewParent = viewParent;
        ownerViewPosition = localPosition;
        ownerViewRotation = visualStartRotation;

        Vector3 worldPosition = ownerViewParent.TransformPoint(ownerViewPosition);
        Quaternion worldRotation = ownerViewParent.rotation * ownerViewRotation;

        if (ownerViewProxy != null)
        {
            rootMeshRenderer.enabled = false;
            ownerViewProxy.SetActive(true);
            ownerViewProxy.transform.SetPositionAndRotation(worldPosition, worldRotation);
        }
        else
        {
            if (visualRoot.parent != null)
                visualRoot.SetParent(null, true);
            visualRoot.SetPositionAndRotation(worldPosition, worldRotation);
        }
    }

    private void EnsureOwnerViewProxy()
    {
        if (ownerViewProxy != null)
            return;

        ownerViewProxy = new GameObject($"{name}_LocalHeldView");
        ownerViewProxy.transform.localScale = transform.lossyScale;

        MeshFilter proxyFilter = ownerViewProxy.AddComponent<MeshFilter>();
        proxyFilter.sharedMesh = rootMeshFilter.sharedMesh;

        MeshRenderer proxyRenderer = ownerViewProxy.AddComponent<MeshRenderer>();
        proxyRenderer.sharedMaterials = rootMeshRenderer.sharedMaterials;
        proxyRenderer.shadowCastingMode = ShadowCastingMode.Off;
        proxyRenderer.receiveShadows = rootMeshRenderer.receiveShadows;
        proxyRenderer.lightProbeUsage = rootMeshRenderer.lightProbeUsage;
        proxyRenderer.reflectionProbeUsage = rootMeshRenderer.reflectionProbeUsage;
        proxyRenderer.sortingLayerID = rootMeshRenderer.sortingLayerID;
        proxyRenderer.sortingOrder = rootMeshRenderer.sortingOrder;
        proxyRenderer.renderingLayerMask = rootMeshRenderer.renderingLayerMask;
        proxyRenderer.motionVectorGenerationMode = rootMeshRenderer.motionVectorGenerationMode;
        ownerViewProxy.SetActive(false);
    }


    public void DetachOwnerView()
    {
        if (!ownerViewAttached || visualRoot == null)
            return;

        if (ownerViewProxy != null)
        {
            ownerViewProxy.SetActive(false);
            if (rootMeshRenderer != null)
                rootMeshRenderer.enabled = rootRendererWasEnabled;
            Destroy(ownerViewProxy);
            ownerViewProxy = null;
        }
        else
        {
            visualRoot.SetParent(originalVisualParent, false);
            visualRoot.localPosition = visualStartPosition;
            visualRoot.localRotation = visualStartRotation;
            visualRoot.localScale = visualStartScale;
        }
        ownerViewAttached = false;
        ownerViewParent = null;
    }

    public void SetOwnerSway(Vector3 localPositionOffset, Quaternion localRotationOffset)
    {
        if (!IsSpawned || visualRoot == null || visualRoot == transform || !ownerViewAttached)
            return;

        if (ownerViewParent == null)
            return;

        Transform viewTransform = ownerViewProxy != null ? ownerViewProxy.transform : visualRoot;
        viewTransform.position = ownerViewParent.TransformPoint(ownerViewPosition + localPositionOffset);
        viewTransform.rotation = ownerViewParent.rotation * ownerViewRotation * localRotationOffset;
        if (ownerViewProxy != null && !ownerViewProxy.activeSelf)
            ownerViewProxy.SetActive(true);
    }

    private void OnHolderChanged(ulong previous, ulong current)
    {
        ApplyHeldPhysics(current != ulong.MaxValue);

        if (ownerViewAttached && current == ulong.MaxValue)
            DetachOwnerView();

        if (visualRoot != null && visualRoot != transform && !ownerViewAttached)
        {
            visualRoot.localPosition = visualStartPosition;
            visualRoot.localRotation = visualStartRotation;
            visualRoot.localScale = visualStartScale;
        }
    }

    private void ApplyHeldPhysics(bool held)
    {
        if (body != null)
        {
            body.isKinematic = held || !IsServer;
            body.useGravity = !held && IsServer;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        if (colliders == null)
            return;

        foreach (Collider itemCollider in colliders)
        {
            if (itemCollider != null)
                itemCollider.enabled = !held;
        }
    }
}
