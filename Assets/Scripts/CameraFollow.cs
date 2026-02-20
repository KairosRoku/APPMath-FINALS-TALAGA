using UnityEngine;

/// <summary>
/// Smooth orthographic camera follow using GameManager's player position.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    [Header("References")]
    public GPUInstanceRenderer gpuRenderer;

    [Header("Settings")]
    public float smoothSpeed = 6f;
    public Vector2 offset    = new Vector2(0f, 1.2f);
    public bool clampToLevel = true;

    Camera _cam;

    void Awake() => _cam = GetComponent<Camera>();

    void LateUpdate()
    {
        if (gpuRenderer == null) return;

        Vector2 target2D = gpuRenderer.GetPlayerPosition() + offset;
        Vector3 target   = new Vector3(target2D.x, target2D.y, transform.position.z);

        if (clampToLevel && gpuRenderer.levelData != null)
        {
            var ld    = gpuRenderer.levelData;
            float hh  = _cam.orthographicSize;
            float hw  = hh * _cam.aspect;
            float minX = ld.gridOrigin.x + hw;
            float maxX = ld.gridOrigin.x + ld.width  * ld.tileSize - hw;
            float minY = ld.gridOrigin.y + hh;
            float maxY = ld.gridOrigin.y + ld.height * ld.tileSize - hh;
            target.x = Mathf.Clamp(target.x, minX, Mathf.Max(minX, maxX));
            target.y = Mathf.Clamp(target.y, minY, Mathf.Max(minY, maxY));
        }

        transform.position = Vector3.Lerp(
            transform.position, target, 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime));
    }
}
