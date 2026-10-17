using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    [SerializeField] private Button _hostButton;
    [SerializeField] private Button _joinButton;
    [SerializeField] private TMP_InputField _inputField;


    private void Awake()
    {
        _hostButton.onClick.AddListener(OnHostButtonClicked);
        _joinButton.onClick.AddListener(OnClientButtonClicked);
    }

    private async void OnHostButtonClicked()
    {
        await HostSingleton.Instance.HostGameManager.StartHostAsync();
    }

    private async void OnClientButtonClicked()
    {
        await ClientSingleton.Instance.ClientGameManager.StartClientAsync(_inputField.text);
    }
}
