using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[DisallowMultipleComponent]
public sealed class ItemDeliveryJob : NetworkBehaviour
{
    private static readonly HashSet<ItemDeliveryJob> ActiveJobs = new HashSet<ItemDeliveryJob>();

    [Header("Delivery Job")]
    [Tooltip("Optional friendly task name. If empty, the item ID and this GameObject's name are used.")]
    [SerializeField] private string _jobName;
    [SerializeField, Min(1)] private int _requiredItemCount = 5;
    [Tooltip("HoldableItem Item ID veya AnimalAI Delivery Item ID ile eşleşmeli.")]
    [SerializeField] private string _requiredItemId = "GenericItem";

    [Header("Trigger")]
    [SerializeField] private Collider _triggerCollider;

    private readonly NetworkVariable<int> _deliveredItemCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> _completed = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly HashSet<ulong> _deliveredItemIds = new HashSet<ulong>();
    private bool _configurationValid;

    public int DeliveredItemCount => _deliveredItemCount.Value;
    public bool IsCompleted => _completed.Value;
    public string JobDisplayName => GetJobName();

    private void Awake()
    {
        if (_triggerCollider == null)
            _triggerCollider = GetComponent<Collider>();

        _configurationValid = ValidateConfiguration();
    }

    private void OnValidate()
    {
        _requiredItemCount = Mathf.Max(1, _requiredItemCount);
        if (_triggerCollider == null)
            _triggerCollider = GetComponent<Collider>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer || !_configurationValid)
            return;

        ActiveJobs.Add(this);
        _deliveredItemIds.Clear();
        _deliveredItemCount.Value = 0;
        _completed.Value = false;

        if (JobManager.Instance == null)
        {
            Debug.LogError("ItemDeliveryJob: GameScene'de JobManager bulunamadı.", this);
            return;
        }

        JobManager.Instance.RegisterJob(JobManager.JobType.ItemDelivery, NetworkObjectId);
        Debug.Log(
            $"[ItemDeliveryJob] Görev başladı: {GetJobName()} — {_requiredItemCount} adet '{_requiredItemId.Trim()}' getir.",
            this);
    }

    public override void OnNetworkDespawn()
    {
        ActiveJobs.Remove(this);

        if (IsServer && JobManager.Instance != null)
            JobManager.Instance.UnregisterJob(JobManager.JobType.ItemDelivery, NetworkObjectId);

        _deliveredItemIds.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        TryAcceptItem(other != null ? other.GetComponentInParent<HoldableItem>() : null);
    }

    private void OnTriggerStay(Collider other)
    {
        // Fallback for an item whose collider became active while it was already in the trigger.
        TryAcceptItem(other != null ? other.GetComponentInParent<HoldableItem>() : null);
    }

    public static void NotifyItemDropped(HoldableItem item)
    {
        if (item == null || !item.IsSpawned || !item.IsServer)
            return;

        foreach (ItemDeliveryJob job in ActiveJobs)
        {
            if (job != null && job.IsItemOverlappingTrigger(item))
                job.TryAcceptItem(item);
        }
    }

    private bool IsItemOverlappingTrigger(HoldableItem item)
    {
        if (!_configurationValid || _completed.Value || !IsSpawned || !isActiveAndEnabled || _triggerCollider == null)
            return false;

        Collider[] itemColliders = item.GetComponentsInChildren<Collider>();
        foreach (Collider itemCollider in itemColliders)
        {
            if (itemCollider == null || !itemCollider.enabled || !itemCollider.gameObject.activeInHierarchy ||
                !_triggerCollider.bounds.Intersects(itemCollider.bounds))
                continue;

            if (!SupportsPenetrationTest(_triggerCollider) || !SupportsPenetrationTest(itemCollider))
                return true;

            if (Physics.ComputePenetration(
                    _triggerCollider,
                    _triggerCollider.transform.position,
                    _triggerCollider.transform.rotation,
                    itemCollider,
                    itemCollider.transform.position,
                    itemCollider.transform.rotation,
                    out _,
                    out _))
                return true;
        }

        return false;
    }

    private static bool SupportsPenetrationTest(Collider target)
    {
        return target is BoxCollider || target is SphereCollider || target is CapsuleCollider ||
               (target is MeshCollider meshCollider && meshCollider.convex);
    }

    private void TryAcceptItem(HoldableItem item)
    {
        if (!IsServer || !IsSpawned || !_configurationValid || _completed.Value || item == null ||
            !item.IsSpawned || !item.CanBePickedUp || item.IsHeld || !MatchesRequiredItem(item))
            return;

        if (!_deliveredItemIds.Add(item.NetworkObjectId))
            return;

        AnimalAI deliveredAnimal = item.GetComponent<AnimalAI>();
        if (deliveredAnimal != null)
            deliveredAnimal.StopAIForDelivery();

        _deliveredItemCount.Value++;
        string jobName = GetJobName();
        Debug.Log($"[ItemDeliveryJob] {jobName}: {_deliveredItemCount.Value}/{_requiredItemCount} eşya", this);

        if (_deliveredItemCount.Value < _requiredItemCount)
            return;

        _completed.Value = true;
        if (JobManager.Instance != null)
            JobManager.Instance.ReportJobCompleted(
                JobManager.JobType.ItemDelivery,
                NetworkObjectId,
                jobName);
    }

    private bool MatchesRequiredItem(HoldableItem item)
    {
        string requiredItemId = _requiredItemId.Trim();
        if (string.Equals(item.ItemId, requiredItemId, StringComparison.OrdinalIgnoreCase))
            return true;

        AnimalAI animal = item.GetComponent<AnimalAI>();
        return animal != null &&
               string.Equals(animal.DeliveryItemId, requiredItemId, StringComparison.OrdinalIgnoreCase);
    }

    private bool ValidateConfiguration()
    {
        if (_triggerCollider == null)
        {
            Debug.LogError("ItemDeliveryJob: Aynı GameObject üzerinde bir Collider olmalı.", this);
            return false;
        }

        if (_triggerCollider.gameObject != gameObject)
        {
            Debug.LogError("ItemDeliveryJob: Trigger Collider, ItemDeliveryJob ile aynı GameObject üzerinde olmalı.", this);
            return false;
        }

        if (!_triggerCollider.isTrigger)
        {
            Debug.LogError("ItemDeliveryJob: Collider üzerinde Is Trigger açılmalı.", this);
            return false;
        }

        if (_requiredItemCount < 1)
        {
            Debug.LogError("ItemDeliveryJob: Gerekli eşya sayısı en az 1 olmalı.", this);
            return false;
        }

        if (string.IsNullOrWhiteSpace(_requiredItemId))
        {
            Debug.LogError("ItemDeliveryJob: Required Item Id boş bırakılamaz.", this);
            return false;
        }

        return true;
    }

    private string GetJobName()
    {
        return string.IsNullOrWhiteSpace(_jobName)
            ? $"{_requiredItemId.Trim()} teslimatı ({gameObject.name})"
            : _jobName.Trim();
    }
}
