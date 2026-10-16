
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
    private float _bobTimer;

    private void Start()
    {
        _startPosition = _cameraTransform.localPosition;
    }

    private void Update()
    {
        if (!IsOwner)
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

        bool isMoving = state == PlayerState.Walk ||
                        state == PlayerState.Run ||
                        (state == PlayerState.Crouch &&
                         (Input.GetAxisRaw("Horizontal") != 0f ||
                          Input.GetAxisRaw("Vertical") != 0f));

        if (isMoving)
        {
            _bobTimer += Time.deltaTime * bobSpeed;

            float bobY = Mathf.Sin(_bobTimer) * bobAmount;
            float bobX = Mathf.Cos(_bobTimer * 0.5f) * bobAmount * 0.5f;

            Vector3 targetPosition = _startPosition +
                                     new Vector3(bobX, bobY, 0f);

            _cameraTransform.localPosition = Vector3.Lerp(
                _cameraTransform.localPosition,
                targetPosition,
                Time.deltaTime * bobSpeed
            );
        }
        else
        {
            _bobTimer = 0f;

            _cameraTransform.localPosition = Vector3.Lerp(
                _cameraTransform.localPosition,
                _startPosition,
                Time.deltaTime * _returnSpeed
            );
        }
    }
}
