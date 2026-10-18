
using UnityEngine;
using Unity.Netcode;

public class PlayerMovement : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody _playerRigidbody;
    [SerializeField] private PlayerDataSO _playerData;
    [SerializeField] private Camera _playerCamera;
    [SerializeField] private PlayerAnimationController _playerAnimationController;

    [Header("Settings")]
    [SerializeField] private PlayerState _currentPlayerState;
    [SerializeField] private float _mouseSensitivity = 150f;

    private float _horizontal;
    private float _vertical;
    private float _mouseX;
    private float _rotationY;

    private bool _isRun;
    private bool _isCrouch;
    private WaterValve _waterValve;

    public override void OnNetworkSpawn()
    {
        _waterValve = GetComponent<WaterValve>();

        if (_playerCamera != null)
            _playerCamera.enabled = IsOwner;
        _rotationY = transform.eulerAngles.y;
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        if (FuseBox.IsAnyPanelOpen)
        {
            _horizontal = 0f;
            _vertical = 0f;
            _mouseX = 0f;
            _isRun = false;
            MoveControll();
            SetPlayerState();
            return;
        }

        if (_waterValve != null && _waterValve.IsInteractionActive)
        {
            _horizontal = 0f;
            _vertical = 0f;
            _mouseX = 0f;
            _isRun = false;
            MoveControll();
            SetPlayerState();
            return;
        }

        MyInput();
        MoveControll();
        SetPlayerState();
    }

    private void FixedUpdate()
    {
        if (!IsOwner)
            return;

        if (_waterValve != null && _waterValve.IsInteractionActive)
        {
            _horizontal = 0f;
            _vertical = 0f;
            _mouseX = 0f;
            MoveControll();
            return;
        }

        Rotate();
        Move();
    }

    private void MyInput()
    {
        _horizontal = Input.GetAxisRaw("Horizontal");
        _vertical = Input.GetAxisRaw("Vertical");

        _mouseX += Input.GetAxis("Mouse X") * _mouseSensitivity;

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

        _playerRigidbody.AddForce(
            Vector3.up * _playerData.JumpForce,
            ForceMode.Impulse
        );
    }

    private void Move()
    {
        Vector3 move = new Vector3(
            _horizontal,
            0f,
            _vertical
        ).normalized;

        float speed = GetPlayerSpeed();

        Vector3 velocity =
            (transform.forward * move.z + transform.right * move.x) * speed;

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
        if (!IsGround())
        {
            _currentPlayerState = PlayerState.Air;
            return;
        }

        if (_isCrouch)
        {
            _currentPlayerState = PlayerState.Crouch;
            return;
        }

        bool hasMovementInput = new Vector2(_horizontal, _vertical).sqrMagnitude > 0.0001f;
        if (!hasMovementInput)
            _currentPlayerState = PlayerState.Idle;
        else
            _currentPlayerState = _isRun ? PlayerState.Run : PlayerState.Walk;
    }

    private void Rotate()
    {
        _rotationY += _mouseX;
        _mouseX = 0f;

        _playerRigidbody.MoveRotation(
            Quaternion.Euler(0f, _rotationY, 0f)
        );
    }

    private bool IsGround()
    {
        return Physics.Raycast(
            transform.position,
            Vector3.down,
            _playerData.PlayerHeight
        );
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
