using UnityEngine;

[CreateAssetMenu(fileName = "PlayerDataSO", menuName = "Scriptable Objects/PlayerDataSO")]
public class PlayerDataSO : ScriptableObject
{
    [SerializeField] private float _playerSpeed;

    [SerializeField] private float _runMultiplier;

    [SerializeField] private float _jumpForce;

    [SerializeField] private float _playerHeight;
    [SerializeField] private float _crouchSpeed;

    public float PlayerSpeed => _playerSpeed;

    public float JumpForce => _jumpForce;

    public float RunMultiplier => _runMultiplier;

    public float PlayerHeight => _playerHeight;

    public float CrouchSpeed => _crouchSpeed;
}
