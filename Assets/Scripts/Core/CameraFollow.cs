using UnityEngine;

/// <summary>
/// Smoothly follows a target Transform while keeping the camera on the XY plane.
/// Z stays fixed at zOffset (-10) so gameplay depth never changes.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("Follow")]
    [SerializeField] private Transform target;
    [SerializeField] private float smoothTime = 0.15f;
    [SerializeField] private float zOffset = -10f;

    [Header("Level bounds (camera center is kept inside)")]
    [SerializeField] private bool useBounds = true;
    [SerializeField] private float minX = -12f;
    [SerializeField] private float maxX = 12f;
    [SerializeField] private float minY = -6f;
    [SerializeField] private float maxY = 6f;

    private Camera _camera;
    private Vector3 _velocity = Vector3.zero;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        if (_camera == null)
        {
            _camera = Camera.main;
        }
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        Vector3 desired = target.position;
        desired.z = zOffset;

        if (useBounds && _camera != null)
        {
            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;

            float centerX = (minX + maxX) * 0.5f;
            float centerY = (minY + maxY) * 0.5f;

            // If the view is wider/taller than the level, level the camera on
            // the level center instead of inverting the clamp (which would fling
            // the camera to an edge).
            float lowX = centerX;
            float highX = centerX;
            float lowY = centerY;
            float highY = centerY;

            float cx = minX + halfWidth;
            float cy = maxX - halfWidth;
            if (cx < cy) { lowX = cx; highX = cy; }

            float ctx = minY + halfHeight;
            float cty = maxY - halfHeight;
            if (ctx < cty) { lowY = ctx; highY = cty; }

            desired.x = Mathf.Clamp(desired.x, lowX, highX);
            desired.y = Mathf.Clamp(desired.y, lowY, highY);
        }

        transform.position = Vector3.SmoothDamp(transform.position, desired, ref _velocity, smoothTime);
    }
}