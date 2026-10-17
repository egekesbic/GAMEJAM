using Cysharp.Threading.Tasks;
using UnityEngine;

public class HostSingleton : MonoBehaviour
{
    private static HostSingleton instance;
    public HostGameManager HostGameManager { get; private set; }
    public static HostSingleton Instance
    {
        get
        {
            if (instance != null)
            {
                return instance;
            }

            instance = FindAnyObjectByType<HostSingleton>();

            if (instance == null)
            {
                Debug.LogError("HostSngeleton yok bomobm");
                return null;
            }

            return instance;
        }
    }

    private void Start()
    {
        DontDestroyOnLoad(gameObject);
    }

    public void CreateHost()
    {
        HostGameManager = new HostGameManager();
    }
}
