using UnityEngine;

/// <summary>
/// Visual-only parallax for a single background layer.
/// Moves this transform by a fraction of the camera's displacement so the
/// layer appears farther away. Never modifies Z (background Z is set once).
/// </summary>
public class ParallaxLayer : MonoBehaviour
{
    [Header("Parallax")]
    [SerializeField] private Transform followCamera;
    [SerializeField, Range(0f, 1f)] private float parallaxFactor = 0.2f;
    [SerializeField] private bool lockY = true;

    private Vector3 _startPosition;
    private Vector3 _cameraStartPosition;
    private bool _initialized;

    private void Start()
    {
        if (followCamera == null)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                followCamera = cam.transform;
            }
        }

        _startPosition = transform.position;

        if (followCamera != null)
        {
            _cameraStartPosition = followCamera.position;
            _initialized = true;
        }
    }

    private void LateUpdate()
    {
        if (!_initialized || followCamera == null)
        {
            return;
        }

        Vector3 cameraDelta = followCamera.position - _cameraStartPosition;
        Vector3 newPosition = _startPosition + (cameraDelta * parallaxFactor);

        if (lockY)
        {
            newPosition.y = _startPosition.y;
        }

        // Z is intentionally left at the value set in the scene.
        transform.position = newPosition;
    }
}