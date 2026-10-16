using Unity.Netcode;
using UnityEngine;

public class CameraController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Transform _playerCamera;

    [Header("Settings")]
    [SerializeField] private float _mouseSensitivity = 150f;

    private float _xRotation;

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        float mouseY = Input.GetAxis("Mouse Y") * _mouseSensitivity * Time.deltaTime;

        _xRotation -= mouseY;
        _xRotation = Mathf.Clamp(_xRotation, -60f, 60f);

        _playerCamera.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);
    }
}