using UnityEngine;
using Unity.Netcode;
using System;

public class PlayerMovement : NetworkBehaviour
{

    [Header("References")]

    [SerializeField] private Rigidbody _playerRigidbody;

    [SerializeField] private PlayerDataSO _playerData;

    [SerializeField] private Camera _playerCamera;

    [SerializeField] private PlayerAnimationController _playerAnimationController;

    private float _horizontal, _vertical;

    private float _mouseX;

    private bool _isRun;

    private bool _isCrouch;

    [SerializeField] private PlayerState _currentPlayerState;

    [SerializeField] private float _mouseSensitivity;

    public override void OnNetworkSpawn()
    {
        _playerCamera.enabled = IsOwner;
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        MoveControll();
        MyInput();
        Rotate();
        SetPlayerState();
    }

    private void FixedUpdate()
    {
        if (!IsOwner)
        {
            return;
        }

        Move();
    }

    private void MyInput()
    {
        _horizontal = Input.GetAxis("Horizontal");
        _vertical = Input.GetAxis("Vertical");

        _mouseX = Input.GetAxis("Mouse X") * _mouseSensitivity;

        if (Input.GetKeyDown(KeyCode.Space) && IsGround())
        {
            Jump();
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            Crouch();
        }

        _isRun = Input.GetKey(KeyCode.LeftShift);
    }

    private void Crouch()
    {
        _isCrouch = !_isCrouch;
    }

    private void Jump()
    {
        _playerAnimationController.JumpAnimation();
        _playerRigidbody.AddForce(Vector3.up * _playerData.JumpForce, ForceMode.Impulse);
    }

    private void Move()
    {
        Vector3 move = new Vector3(_horizontal, 0f, _vertical).normalized;

        float speed = GetPlayerSpeed();

        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        Vector3 velocity = (forward * move.z + right * move.x)
                           * speed;

        _playerRigidbody.linearVelocity = new Vector3(
            velocity.x,
            _playerRigidbody.linearVelocity.y,
            velocity.z
        );
    }

    private float GetPlayerSpeed()
    {
        return _currentPlayerState switch
        {
            PlayerState.Idle => 0f,
            PlayerState.Walk => _playerData.PlayerSpeed,
            PlayerState.Run => _playerData.RunMultiplier * _playerData.PlayerSpeed,
            PlayerState.Crouch => _playerData.CrouchSpeed,
            PlayerState.Air => _playerData.PlayerSpeed,
            _ => _playerData.PlayerSpeed
        };
    }

    private void MoveControll()
    {
        if (_horizontal == 0f && _vertical == 0f)
        {
            _playerRigidbody.linearVelocity = new Vector3(
                0f,
                _playerRigidbody.linearVelocity.y,
                0f
            );
        }
    }

    private void SetPlayerState()
    {
        PlayerState newPlayerState = _currentPlayerState switch
        {
            PlayerState.Idle => _isCrouch ? PlayerState.Crouch :
                                _isRun ? PlayerState.Run :
                                (_horizontal != 0f || _vertical != 0f) ? PlayerState.Walk :
                                PlayerState.Idle,

            PlayerState.Walk => !IsGround() ? PlayerState.Air :
                                _isCrouch ? PlayerState.Crouch :
                                _isRun ? PlayerState.Run :
                                (_horizontal == 0f && _vertical == 0f) ? PlayerState.Idle :
                                PlayerState.Walk,

            PlayerState.Run => !IsGround() ? PlayerState.Air :
                               _isCrouch ? PlayerState.Crouch :
                               !_isRun ? PlayerState.Walk :
                               PlayerState.Run,

            PlayerState.Crouch => !IsGround() ? PlayerState.Air :
                                  !_isCrouch ? PlayerState.Idle :
                                  PlayerState.Crouch,

            PlayerState.Air => IsGround() ? PlayerState.Idle :
                               PlayerState.Air,

            _ => PlayerState.Idle
        };

        _currentPlayerState = newPlayerState;
    }

    private void Rotate()
    {
        transform.Rotate(0f, _mouseX * Time.deltaTime, 0f);
    }

    private bool IsGround()
    {
        return Physics.Raycast(transform.position, Vector3.down, _playerData.PlayerHeight);
    }

    public PlayerState GetPlayerState()
    {
        return _currentPlayerState;
    }

    public float GetHorizontal()
    {
        return _horizontal;
    }

    public float GetVertical()
    {
        return _vertical;
    }

    public bool IsGrounded()
    {
        return IsGround();
    }
}
