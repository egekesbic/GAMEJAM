
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class PlayerInteraction : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Camera _playerCamera;
    [SerializeField] private TMP_Text _interactionText;

    [Header("Interaction Settings")]
    [SerializeField] private float _interactionDistance = 3f;
    [SerializeField] private KeyCode _interactionKey = KeyCode.F;

    private UpgradeWindow _targetWindow;

    private void Start()
    {
        if (_interactionText != null)
            _interactionText.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        DetectWindow();

        if (_targetWindow != null &&
            _targetWindow.CanRepair &&
            Input.GetKeyDown(_interactionKey))
        {
            _targetWindow.RepairServerRpc();
        }
    }

    private void DetectWindow()
    {
        _targetWindow = null;

        if (_playerCamera == null)
        {
            SetPrompt(false);
            return;
        }

        Ray ray = _playerCamera.ViewportPointToRay(
            new Vector3(0.5f, 0.5f, 0f)
        );

        if (Physics.Raycast(
                ray,
                out RaycastHit hit,
                _interactionDistance))
        {
            _targetWindow =
                hit.collider.GetComponentInParent<UpgradeWindow>();
        }

        bool canRepair =
            _targetWindow != null && _targetWindow.CanRepair;

        SetPrompt(canRepair);
    }

    private void SetPrompt(bool visible)
    {
        if (_interactionText == null)
            return;

        _interactionText.gameObject.SetActive(visible);

        if (visible)
            _interactionText.text = "F - Repair Barricade";
    }
}
