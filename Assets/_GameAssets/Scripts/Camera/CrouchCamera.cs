
using Unity.Netcode;
using UnityEngine;

public class CrouchCamera : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerMovement _playerMovement;
    [SerializeField] private Transform _cameraTransform;

    [Header("Settings")]
    [SerializeField] private float _crouchCameraOffset = 0.5f;
    [SerializeField] private float _transitionSpeed = 8f;

    private Vector3 _standingPosition;
    private Vector3 _basePosition;

    public Vector3 BasePosition => _basePosition;

    private void Start()
    {
        if (_cameraTransform == null)
            return;

        _standingPosition = _cameraTransform.localPosition;
        _basePosition = _standingPosition;
    }

    private void Update()
    {
        if (!IsOwner || _playerMovement == null || _cameraTransform == null)
        {
            return;
        }

        Vector3 targetPosition = _standingPosition;
        if (_playerMovement.GetPlayerState() == PlayerState.Crouch)
            targetPosition.y -= _crouchCameraOffset;

        _basePosition = Vector3.Lerp(_basePosition, targetPosition, Time.deltaTime * _transitionSpeed);
        _cameraTransform.localPosition = _basePosition;
    }
}
