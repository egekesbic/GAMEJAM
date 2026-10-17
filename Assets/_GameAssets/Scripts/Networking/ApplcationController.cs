using Cysharp.Threading.Tasks;
using System.Threading.Tasks;
using UnityEngine;

public class ApplcationController : MonoBehaviour
{
    [SerializeField] private ClientSingleton _clientSingletonPrefab;
    [SerializeField] private HostSingleton _hostSingletonPrefab;

    private async void Start()
    {
        DontDestroyOnLoad(gameObject);

        await LaunchInMode(SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null);
    }

    private async UniTask LaunchInMode(bool isDedicatedServer)
    {
        if (isDedicatedServer)
        {

        }
        else
        {
            HostSingleton hostSingleton = Instantiate(_hostSingletonPrefab);
            hostSingleton.CreateHost();

            ClientSingleton clientSingletonInstance = Instantiate(_clientSingletonPrefab);
            bool isAuthendicated = await clientSingletonInstance.CreateClient();

            if (isAuthendicated)
            {
                clientSingletonInstance.ClientGameManager.GoToMainMenu();
            }


        }
    }
}
