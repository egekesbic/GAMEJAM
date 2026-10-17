using Cysharp.Threading.Tasks;
using UnityEngine;

public class ClientSingleton : MonoBehaviour
{
    private static ClientSingleton instance;
    public ClientGameManager ClientGameManager { get; private set; }
    public static ClientSingleton Instance
    {
        get
        {
            if(instance != null) 
            { 
                return instance; 
            }

            instance = FindAnyObjectByType<ClientSingleton>();

            if (instance == null)
            {
                Debug.LogError("ClientSngeleton yok bomobm");
                return null;
            }

            return instance;
        }
    }

    private void Start()
    {
        DontDestroyOnLoad(gameObject);
    }

    public async UniTask<bool> CreateClient()
    {
        ClientGameManager = new ClientGameManager();
        return await ClientGameManager.InitAsync();
    }
}
