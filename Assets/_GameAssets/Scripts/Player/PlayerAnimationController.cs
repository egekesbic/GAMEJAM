
using Unity.Netcode;
using UnityEngine;

public class PlayerAnimationController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Animator _playerAnimator;
    [SerializeField] private PlayerMovement _playerMovement;

    private static readonly int HorizontalHash = Animator.StringToHash("Horizontal");
    private static readonly int VerticalHash = Animator.StringToHash("Vertical");
    private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");
    private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int Jump = Animator.StringToHash("Jump");
    private static readonly int IsCrouchingHash = Animator.StringToHash("IsCrouching");

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        if (_playerMovement == null || _playerAnimator == null)
        {
            return;
        }

        PlayerState state = _playerMovement.GetPlayerState();

        _playerAnimator.SetFloat(
            HorizontalHash,
            _playerMovement.GetHorizontal()
        );

        _playerAnimator.SetFloat(
            VerticalHash,
            _playerMovement.GetVertical()
        );

        _playerAnimator.SetBool(
            IsRunningHash,
            state == PlayerState.Run
        );

        _playerAnimator.SetBool(
            IsGroundedHash,
            _playerMovement.IsGrounded()
        );

        _playerAnimator.SetBool(
            IsCrouchingHash,
            state == PlayerState.Crouch
        );
    }

    public void JumpAnimation()
    {
        _playerAnimator.SetTrigger(Jump);
    }
}
