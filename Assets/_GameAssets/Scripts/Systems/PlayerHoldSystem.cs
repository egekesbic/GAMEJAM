using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Add to the player NetworkObject. The owning player aims and sends a request;
/// the server checks it, then performs the pickup/drop/throw.
/// </summary>
public sealed class PlayerHoldSystem : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private TMP_Text holdPrompt;
    [Tooltip("Assign the actual Camera GameObject here if it is not under the player prefab. Otherwise it is auto-detected.")]
    [SerializeField] private GameObject cameraReference;

    [Header("Camera-center ray position")]
    [Tooltip("If the center ray hits a collider, the item uses that point. Otherwise it uses this distance from the camera.")]
    [SerializeField, Min(0.1f)] private float rayPositionDistance = 2.5f;
    [SerializeField] private LayerMask rayPositionLayers = Physics.DefaultRaycastLayers;
    [SerializeField, Min(0f)] private float surfaceOffset = 0.03f;

    [Header("Interaction")]
    [SerializeField, Min(0.1f)] private float pickupDistance = 3f;
    [SerializeField] private KeyCode interactKey = KeyCode.F;
    [SerializeField] private KeyCode throwKey = KeyCode.G;
    [SerializeField, Min(0f)] private float throwForce = 8f;
    [SerializeField, Min(0f)] private float throwUpwardForce = 1f;
    [Tooltip("Server validation limit from the player root to this player's camera position.")]
    [SerializeField, Min(1f)] private float maxHoldPointDistanceFromPlayer = 4f;
    [Tooltip("How often the owning client sends the held item's ray position to the server.")]
    [SerializeField, Min(0.02f)] private float heldPoseSendInterval = 0.05f;

    [Header("Owner-only sway")]
    [SerializeField, Min(0f)] private float swayFrequency = 8f;
    [SerializeField, Min(0f)] private float swayPositionAmount = 0.025f;
    [SerializeField, Min(0f)] private float swayRotationAmount = 2f;

    private HoldableItem locallyHeldItem;
    private HoldableItem aimedItem;
    private float swayTime;
    private float heldPoseSendTimer;

    private void Start()
    {
        if (holdPrompt != null)
            holdPrompt.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!IsOwner || !IsSpawned)
            return;

        EnsureCameraReference();

        if (locallyHeldItem != null && !locallyHeldItem.IsHeldBy(OwnerClientId))
        {
            locallyHeldItem.DetachOwnerView();
            locallyHeldItem = null;
        }

        if (locallyHeldItem == null)
            FindLocallyHeldItem();

        UpdateHeldCameraView();
        UpdateServerHeldPose();

        aimedItem = FindAimedItem();
        UpdatePrompt();

        if (Input.GetKeyDown(interactKey))
        {
            if (locallyHeldItem != null)
                SendDropRequest();
            else
                TryRequestPickup();
        }

        if (locallyHeldItem != null && Input.GetKeyDown(throwKey))
        {
            SendThrowRequest();
        }

        UpdateOwnerSway();
    }

    private void EnsureCameraReference()
    {
        // Keep the explicitly assigned camera. Camera.main is only a last-resort
        // fallback: in multiplayer it can belong to another player or scene camera.
        if (playerCamera != null && playerCamera.isActiveAndEnabled)
            return;

        if (cameraReference != null)
        {
            playerCamera = cameraReference.GetComponent<Camera>();
            if (playerCamera == null)
                playerCamera = cameraReference.GetComponentInChildren<Camera>(true);
        }

        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>(true);

        if (playerCamera == null && IsOwner && Camera.main != null &&
            Vector3.Distance(transform.position, Camera.main.transform.position) <= maxHoldPointDistanceFromPlayer)
        {
            playerCamera = Camera.main;
        }

        if (playerCamera != null && cameraReference == null)
            cameraReference = playerCamera.gameObject;
    }

    private void UpdateHeldCameraView()
    {
        if (locallyHeldItem == null || playerCamera == null)
            return;

        Ray centerRay = GetCameraCenterRay();
        Vector3 worldPosition = GetRayPosition(centerRay);
        Vector3 cameraLocalPosition = playerCamera.transform.InverseTransformPoint(worldPosition);
        locallyHeldItem.AttachOwnerView(playerCamera.transform, cameraLocalPosition);
    }

    private void UpdateServerHeldPose()
    {
        if (locallyHeldItem == null || playerCamera == null)
            return;

        heldPoseSendTimer -= Time.deltaTime;
        if (heldPoseSendTimer > 0f)
            return;

        heldPoseSendTimer = heldPoseSendInterval;
        Ray ray = GetCameraCenterRay();
        UpdateHeldPoseRpc(ray.origin, ray.direction, playerCamera.transform.rotation);
    }

    private Ray GetCameraCenterRay()
    {
        return playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
    }

    private Vector3 GetRayPosition(Ray ray)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            rayPositionDistance,
            rayPositionLayers,
            QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            Transform hitTransform = hit.collider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
                continue;

            return hit.point + hit.normal * surfaceOffset;
        }

        return ray.GetPoint(rayPositionDistance);
    }

    private void SendDropRequest()
    {
        if (playerCamera == null)
            return;

        Ray ray = GetCameraCenterRay();
        RequestDropRpc(ray.origin, ray.direction, playerCamera.transform.rotation);
    }

    private void SendThrowRequest()
    {
        if (playerCamera == null)
            return;

        Ray ray = GetCameraCenterRay();
        RequestThrowRpc(ray.origin, ray.direction, playerCamera.transform.rotation);
    }

    private HoldableItem FindAimedItem()
    {
        if (playerCamera == null)
            return null;

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        return RaycastForItem(ray, pickupDistance, out HoldableItem item) ? item : null;
    }

    private void UpdatePrompt()
    {
        if (holdPrompt == null)
            return;

        if (locallyHeldItem != null)
        {
            holdPrompt.text = "F - Bırak    G - Fırlat";
            holdPrompt.gameObject.SetActive(true);
        }
        else if (aimedItem != null && aimedItem.CanBePickedUp && !aimedItem.IsHeld)
        {
            holdPrompt.text = "F - Al";
            holdPrompt.gameObject.SetActive(true);
        }
        else
        {
            holdPrompt.gameObject.SetActive(false);
        }
    }

    private void TryRequestPickup()
    {
        if (playerCamera == null || aimedItem == null || !aimedItem.IsSpawned)
            return;

        Transform cameraTransform = playerCamera.transform;
        RequestPickupRpc(
            new NetworkObjectReference(aimedItem.NetworkObject),
            cameraTransform.position,
            cameraTransform.rotation);
    }

    private void FindLocallyHeldItem()
    {
        if (NetworkManager == null || NetworkManager.SpawnManager == null)
            return;

        foreach (NetworkObject networkObject in NetworkManager.SpawnManager.SpawnedObjectsList)
        {
            HoldableItem item = networkObject.GetComponent<HoldableItem>();
            if (item != null && item.IsHeldBy(OwnerClientId))
            {
                locallyHeldItem = item;
                return;
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestPickupRpc(
        NetworkObjectReference itemReference,
        Vector3 cameraPosition,
        Quaternion cameraRotation,
        RpcParams rpcParams = default)
    {
        if (!IsServer || !IsSpawned || rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (!itemReference.TryGet(out NetworkObject itemObject) || itemObject == null)
            return;

        HoldableItem item = itemObject.GetComponent<HoldableItem>();
        if (item == null || !item.CanBePickedUp || item.IsHeld || GetServerHeldItem() != null)
            return;

        // Check the client camera is near their player and that the server ray hits this item.
        if (Vector3.Distance(transform.position, cameraPosition) > maxHoldPointDistanceFromPlayer)
            return;

        Vector3 viewDirection = cameraRotation * Vector3.forward;
        if (Vector3.Distance(cameraPosition, item.transform.position) > pickupDistance + 0.5f)
            return;

        Ray serverRay = new Ray(cameraPosition, viewDirection);
        if (!RaycastForItem(serverRay, pickupDistance + 0.5f, out HoldableItem hitItem) || hitItem != item)
        {
            return;
        }

        Vector3 holdPosition = GetRayPosition(new Ray(cameraPosition, viewDirection));
        item.ServerPickUp(NetworkObject, holdPosition, cameraRotation);
    }

    private bool RaycastForItem(Ray ray, float distance, out HoldableItem item)
    {
        item = null;
        RaycastHit[] hits = Physics.RaycastAll(ray, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            Transform hitTransform = hit.collider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
                continue;

            item = hit.collider.GetComponentInParent<HoldableItem>();
            return item != null;
        }

        return false;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestDropRpc(Vector3 rayOrigin, Vector3 rayDirection, Quaternion dropRotation, RpcParams rpcParams = default)
    {
        if (!IsServer || rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (Vector3.Distance(transform.position, rayOrigin) > maxHoldPointDistanceFromPlayer || rayDirection.sqrMagnitude < 0.9f)
            return;

        HoldableItem item = GetServerHeldItem();
        if (item != null)
        {
            Vector3 dropPosition = GetRayPosition(new Ray(rayOrigin, rayDirection.normalized));
            item.ServerDrop(Vector3.zero, dropPosition, dropRotation);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void UpdateHeldPoseRpc(Vector3 rayOrigin, Vector3 rayDirection, Quaternion viewRotation, RpcParams rpcParams = default)
    {
        if (!IsServer || rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (Vector3.Distance(transform.position, rayOrigin) > maxHoldPointDistanceFromPlayer || rayDirection.sqrMagnitude < 0.9f)
            return;

        HoldableItem item = GetServerHeldItem();
        if (item == null)
            return;

        Vector3 heldPosition = GetRayPosition(new Ray(rayOrigin, rayDirection.normalized));
        item.ServerSetHeldPose(OwnerClientId, heldPosition, viewRotation);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestThrowRpc(Vector3 rayOrigin, Vector3 rayDirection, Quaternion dropRotation, RpcParams rpcParams = default)
    {
        if (!IsServer || rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        if (Vector3.Distance(transform.position, rayOrigin) > maxHoldPointDistanceFromPlayer || rayDirection.sqrMagnitude < 0.9f)
            return;

        HoldableItem item = GetServerHeldItem();
        if (item == null)
            return;

        Vector3 dropPosition = GetRayPosition(new Ray(rayOrigin, rayDirection.normalized));
        Vector3 direction = rayDirection.normalized;
        item.ServerDrop(direction * throwForce + Vector3.up * throwUpwardForce, dropPosition, dropRotation);
    }

    private HoldableItem GetServerHeldItem()
    {
        if (NetworkManager == null || NetworkManager.SpawnManager == null)
            return null;

        foreach (NetworkObject networkObject in NetworkManager.SpawnManager.SpawnedObjectsList)
        {
            HoldableItem item = networkObject.GetComponent<HoldableItem>();
            if (item != null && item.IsHeldBy(OwnerClientId))
                return item;
        }

        return null;
    }

    private void UpdateOwnerSway()
    {
        if (locallyHeldItem == null || !locallyHeldItem.IsHeldBy(OwnerClientId))
            return;

        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        float movement = Mathf.Clamp01(new Vector2(horizontal, vertical).magnitude);
        swayTime += Time.deltaTime * swayFrequency * (0.25f + movement);

        Vector3 positionOffset = new Vector3(
            Mathf.Sin(swayTime) * swayPositionAmount * movement,
            Mathf.Abs(Mathf.Cos(swayTime)) * swayPositionAmount * movement,
            0f);
        Vector3 rotationEuler = new Vector3(
            Mathf.Cos(swayTime) * swayRotationAmount * movement,
            0f,
            -Mathf.Sin(swayTime) * swayRotationAmount * movement);

        locallyHeldItem.SetOwnerSway(positionOffset, Quaternion.Euler(rotationEuler));
    }

    public override void OnNetworkDespawn()
    {
        if (locallyHeldItem != null && IsOwner)
        {
            locallyHeldItem.SetOwnerSway(Vector3.zero, Quaternion.identity);
            locallyHeldItem.DetachOwnerView();
        }

        locallyHeldItem = null;
        base.OnNetworkDespawn();
    }
}
