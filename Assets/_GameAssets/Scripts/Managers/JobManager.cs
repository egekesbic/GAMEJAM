using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public sealed class JobManager : MonoBehaviour
{
    public enum JobType
    {
        UpgradeWindow,
        FuseBox,
        WaterValve,
        ItemDelivery
    }

    private sealed class JobProgress
    {
        public readonly HashSet<ulong> Registered = new HashSet<ulong>();
        public readonly HashSet<ulong> Completed = new HashSet<ulong>();
        public readonly Dictionary<ulong, string> DisplayNames = new Dictionary<ulong, string>();
    }

    public readonly struct TaskSnapshot
    {
        public readonly ulong NetworkObjectId;
        public readonly JobType Type;
        public readonly string DisplayName;
        public readonly bool IsCompleted;

        public TaskSnapshot(ulong networkObjectId, JobType type, string displayName, bool isCompleted)
        {
            NetworkObjectId = networkObjectId;
            Type = type;
            DisplayName = displayName;
            IsCompleted = isCompleted;
        }
    }

    public static JobManager Instance { get; private set; }
    public event System.Action MatchProgressChanged;

    private readonly Dictionary<JobType, JobProgress> _progressByType =
        new Dictionary<JobType, JobProgress>();

    private static bool IsServer =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("Birden fazla JobManager bulundu. GameScene'de tek bir JobManager olmalı.", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void RegisterJob(JobType jobType, ulong networkObjectId)
    {
        if (!IsServer || networkObjectId == 0)
            return;

        JobProgress progress = GetProgress(jobType);
        if (!progress.DisplayNames.ContainsKey(networkObjectId))
            progress.DisplayNames.Add(networkObjectId, ResolveDisplayName(jobType, networkObjectId));

        if (progress.Registered.Add(networkObjectId))
            MatchProgressChanged?.Invoke();
    }

    public void UnregisterJob(JobType jobType, ulong networkObjectId)
    {
        if (!IsServer || networkObjectId == 0 ||
            !_progressByType.TryGetValue(jobType, out JobProgress progress))
            return;

        // Completed jobs remain in the match total after their NetworkObject despawns.
        if (progress.Completed.Contains(networkObjectId))
            return;

        bool changed = progress.Registered.Remove(networkObjectId);
        changed |= progress.DisplayNames.Remove(networkObjectId);
        if (changed)
            MatchProgressChanged?.Invoke();
    }

    public void ReportJobCompleted(JobType jobType, ulong networkObjectId, string jobName = null)
    {
        if (!IsServer || networkObjectId == 0)
            return;

        JobProgress progress = GetProgress(jobType);
        if (!progress.Completed.Add(networkObjectId))
            return;

        progress.Registered.Add(networkObjectId);
        if (!string.IsNullOrWhiteSpace(jobName))
            progress.DisplayNames[networkObjectId] = jobName.Trim();
        else if (!progress.DisplayNames.ContainsKey(networkObjectId))
            progress.DisplayNames.Add(networkObjectId, ResolveDisplayName(jobType, networkObjectId));

        int total = CountKnownJobs(progress);
        if (jobType == JobType.UpgradeWindow)
        {
            int completedWindows = Mathf.Min(progress.Completed.Count, total);
            Debug.Log($"[JobManager] Window Repair progress: {completedWindows}/{total} windows", this);
            if (total > 0 && completedWindows >= total)
                Debug.Log("[JobManager] Window Repair objective completed.", this);
        }
        else
        {
            string displayName = string.IsNullOrWhiteSpace(jobName) ? GetJobName(jobType) : jobName.Trim();
            Debug.Log($"[JobManager] {displayName} görevi tamamlandı: {progress.Completed.Count}/{total}", this);
        }

        MatchProgressChanged?.Invoke();
    }

    public void GetMatchProgress(out int completedJobCount, out int totalJobCount)
    {
        completedJobCount = 0;
        totalJobCount = 0;
        if (!IsServer)
            return;

        foreach (KeyValuePair<JobType, JobProgress> pair in _progressByType)
        {
            JobProgress progress = pair.Value;
            int knownJobs = CountKnownJobs(progress);
            if (knownJobs <= 0)
                continue;

            if (pair.Key == JobType.UpgradeWindow)
            {
                // All window repairs form one objective in the match score.
                totalJobCount++;
                if (progress.Completed.Count >= knownJobs)
                    completedJobCount++;
            }
            else
            {
                completedJobCount += progress.Completed.Count;
                totalJobCount += knownJobs;
            }
        }
    }

    public List<TaskSnapshot> GetTaskSnapshots()
    {
        List<TaskSnapshot> snapshots = new List<TaskSnapshot>();
        if (!IsServer)
            return snapshots;

        foreach (KeyValuePair<JobType, JobProgress> pair in _progressByType)
        {
            JobProgress progress = pair.Value;
            foreach (ulong networkObjectId in progress.Registered)
            {
                string displayName = progress.DisplayNames.TryGetValue(networkObjectId, out string registeredName)
                    ? registeredName
                    : GetDisplayName(pair.Key);
                snapshots.Add(new TaskSnapshot(
                    networkObjectId,
                    pair.Key,
                    displayName,
                    progress.Completed.Contains(networkObjectId)));
            }

            foreach (ulong networkObjectId in progress.Completed)
            {
                if (progress.Registered.Contains(networkObjectId))
                    continue;

                string displayName = progress.DisplayNames.TryGetValue(networkObjectId, out string completedName)
                    ? completedName
                    : GetDisplayName(pair.Key);
                snapshots.Add(new TaskSnapshot(networkObjectId, pair.Key, displayName, true));
            }
        }

        snapshots.Sort(CompareSnapshots);
        return snapshots;
    }

    private JobProgress GetProgress(JobType jobType)
    {
        if (!_progressByType.TryGetValue(jobType, out JobProgress progress))
        {
            progress = new JobProgress();
            _progressByType.Add(jobType, progress);
        }

        return progress;
    }

    private static int CountKnownJobs(JobProgress progress)
    {
        int total = progress.Registered.Count;
        foreach (ulong completedJobId in progress.Completed)
        {
            if (!progress.Registered.Contains(completedJobId))
                total++;
        }

        return total;
    }

    private static string GetJobName(JobType jobType)
    {
        switch (jobType)
        {
            case JobType.UpgradeWindow:
                return "UpgradeWindow";
            case JobType.FuseBox:
                return "FuseBox";
            case JobType.WaterValve:
                return "WaterValve";
            case JobType.ItemDelivery:
                return "ItemDelivery";
            default:
                return jobType.ToString();
        }
    }

    private static string ResolveDisplayName(JobType jobType, ulong networkObjectId)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.SpawnManager != null &&
            manager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject networkObject) &&
            networkObject != null)
        {
            if (jobType == JobType.ItemDelivery && networkObject.TryGetComponent(out ItemDeliveryJob deliveryJob))
                return deliveryJob.JobDisplayName;

            return $"{GetDisplayName(jobType)} ({networkObject.gameObject.name})";
        }

        return GetDisplayName(jobType);
    }

    private static string GetDisplayName(JobType jobType)
    {
        switch (jobType)
        {
            case JobType.UpgradeWindow:
                return "Pencereyi tahtalarla güçlendir";
            case JobType.FuseBox:
                return "Sigorta kutusunu tamir et";
            case JobType.WaterValve:
                return "Su vanasını düzelt";
            case JobType.ItemDelivery:
                return "Gerekli eşyaları teslim et";
            default:
                return jobType.ToString();
        }
    }

    private static int CompareSnapshots(TaskSnapshot left, TaskSnapshot right)
    {
        int typeOrder = left.Type.CompareTo(right.Type);
        if (typeOrder != 0)
            return typeOrder;

        int nameOrder = string.Compare(left.DisplayName, right.DisplayName, System.StringComparison.OrdinalIgnoreCase);
        return nameOrder != 0 ? nameOrder : left.NetworkObjectId.CompareTo(right.NetworkObjectId);
    }
}
