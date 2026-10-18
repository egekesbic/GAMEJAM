
using Unity.Netcode;
using UnityEngine;

public class HeadBob : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerMovement _playerMovement;
    [SerializeField] private Transform _cameraTransform;

    [Header("Walk Settings")]
    [SerializeField] private float _walkBobSpeed = 10f;
    [SerializeField] private float _walkBobAmount = 0.05f;

    [Header("Run Settings")]
    [SerializeField] private float _runBobSpeed = 15f;
    [SerializeField] private float _runBobAmount = 0.09f;

    [Header("Crouch Settings")]
    [SerializeField] private float _crouchBobSpeed = 6f;
    [SerializeField] private float _crouchBobAmount = 0.025f;

    [Header("Smoothing")]
    [SerializeField] private float _returnSpeed = 8f;

    private Vector3 _startPosition;
    private Vector3 _bobOffset;
    private float _bobTimer;
    private CrouchCamera _crouchCamera;

    private void Start()
    {
        if (_cameraTransform != null)
            _startPosition = _cameraTransform.localPosition;

        _crouchCamera = GetComponent<CrouchCamera>();
    }

    private void LateUpdate()
    {
        if (!IsOwner || _playerMovement == null || _cameraTransform == null)
        {
            return;
        }

        PlayerState state = _playerMovement.GetPlayerState();

        float bobSpeed = 0f;
        float bobAmount = 0f;

        switch (state)
        {
            case PlayerState.Walk:
                bobSpeed = _walkBobSpeed;
                bobAmount = _walkBobAmount;
                break;

            case PlayerState.Run:
                bobSpeed = _runBobSpeed;
                bobAmount = _runBobAmount;
                break;

            case PlayerState.Crouch:
                bobSpeed = _crouchBobSpeed;
                bobAmount = _crouchBobAmount;
                break;

            case PlayerState.Idle:
            case PlayerState.Air:
                bobSpeed = 0f;
                bobAmount = 0f;
                break;
        }

        bool hasMovementInput = new Vector2(
            _playerMovement.GetHorizontal(),
            _playerMovement.GetVertical()).sqrMagnitude > 0.0001f;
        bool isMoving = hasMovementInput &&
                        (state == PlayerState.Walk ||
                         state == PlayerState.Run ||
                         state == PlayerState.Crouch);

        Vector3 targetOffset = Vector3.zero;
        if (isMoving)
        {
            _bobTimer += Time.deltaTime * bobSpeed;

            float bobY = Mathf.Sin(_bobTimer) * bobAmount;
            float bobX = Mathf.Cos(_bobTimer * 0.5f) * bobAmount * 0.5f;

            targetOffset = new Vector3(bobX, bobY, 0f);
            _bobOffset = Vector3.Lerp(_bobOffset, targetOffset, Time.deltaTime * bobSpeed);
        }
        else
        {
            _bobTimer = 0f;
            _bobOffset = Vector3.Lerp(_bobOffset, targetOffset, Time.deltaTime * _returnSpeed);
        }

        Vector3 basePosition = _crouchCamera != null
            ? _crouchCamera.BasePosition
            : _startPosition;
        _cameraTransform.localPosition = basePosition + _bobOffset;
    }
}
