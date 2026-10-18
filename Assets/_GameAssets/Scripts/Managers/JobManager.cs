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
    }

    public static JobManager Instance { get; private set; }

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

        GetProgress(jobType).Registered.Add(networkObjectId);
    }

    public void UnregisterJob(JobType jobType, ulong networkObjectId)
    {
        if (!IsServer || networkObjectId == 0 ||
            !_progressByType.TryGetValue(jobType, out JobProgress progress))
            return;

        // Completed jobs remain in the match total after their NetworkObject despawns.
        if (!progress.Completed.Contains(networkObjectId))
            progress.Registered.Remove(networkObjectId);
    }

    public void ReportJobCompleted(JobType jobType, ulong networkObjectId, string jobName = null)
    {
        if (!IsServer || networkObjectId == 0)
            return;

        JobProgress progress = GetProgress(jobType);
        if (!progress.Completed.Add(networkObjectId))
            return;

        progress.Registered.Add(networkObjectId);
        int total = CountKnownJobs(progress);
        string displayName = string.IsNullOrWhiteSpace(jobName) ? GetJobName(jobType) : jobName.Trim();
        Debug.Log($"[JobManager] {displayName} görevi tamamlandı: {progress.Completed.Count}/{total}", this);
    }

    public void GetMatchProgress(out int completedJobCount, out int totalJobCount)
    {
        completedJobCount = 0;
        totalJobCount = 0;
        if (!IsServer)
            return;

        foreach (JobProgress progress in _progressByType.Values)
        {
            completedJobCount += progress.Completed.Count;
            totalJobCount += CountKnownJobs(progress);
        }
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
}
