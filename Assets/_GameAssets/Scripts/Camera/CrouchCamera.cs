
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

    private float _standingHeight;

    private void Start()
    {
        _standingHeight = _cameraTransform.localPosition.y;
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        float targetHeight = _playerMovement.GetPlayerState() == PlayerState.Crouch
            ? _standingHeight - _crouchCameraOffset
            : _standingHeight;

        Vector3 targetPosition = _cameraTransform.localPosition;
        targetPosition.y = targetHeight;

        _cameraTransform.localPosition = Vector3.Lerp(
            _cameraTransform.localPosition,
            targetPosition,
            Time.deltaTime * _transitionSpeed
        );
    }
}
