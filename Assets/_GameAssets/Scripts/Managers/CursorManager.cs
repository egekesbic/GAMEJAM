
using UnityEngine;

public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance;

    private static bool _isHide = true;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public static void ChangeCursorMode()
    {
        _isHide = !_isHide;

        if (_isHide)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
