using UnityEngine;

/// <summary>
/// Scene entry point.
/// Creates the procedural quad mesh and wires all runtime references.
/// Attach this to a single GameObject called "Bootstrap" in your scene.
/// </summary>
[DefaultExecutionOrder(-50)] // Must run before GPUInstanceRenderer.Start()
public class BootstrapManager : MonoBehaviour
{
    [Header("Systems (set in Inspector)")]
    public GPUInstanceRenderer gpuRenderer;
    public CameraFollow        cameraFollow;
    public LevelData           levelData;

    [Header("Materials (set in Inspector)")]
    public Material tileMaterial;
    public Material playerMaterial;

    [Header("Compute Shader")]
    public ComputeShader physicsCompute;

    void Awake()
    {
        // Build a unit quad mesh procedurally so we need no external asset
        Mesh quad = BuildQuadMesh();

        // Wire everything into the renderer
        gpuRenderer.physicsCompute = physicsCompute;
        gpuRenderer.levelData      = levelData;
        gpuRenderer.quadMesh       = quad;
        gpuRenderer.tileMaterial   = tileMaterial;
        gpuRenderer.playerMaterial = playerMaterial;

        // Wire camera to renderer
        if (cameraFollow != null)
            cameraFollow.gpuRenderer = gpuRenderer;
    }

    /// <summary>
    /// Generates a unit quad mesh centered at origin (XY plane).
    /// Vertices at ±0.5 on X and Y, normal pointing +Z.
    /// </summary>
    static Mesh BuildQuadMesh()
    {
        var mesh = new Mesh { name = "ProceduralQuad" };

        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f),
        };

        mesh.uv = new Vector2[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
        };

        mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
        mesh.normals   = new Vector3[] {
            Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward
        };

        mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
        mesh.UploadMeshData(false); // keep read-write for bounds calculation
        return mesh;
    }
}
