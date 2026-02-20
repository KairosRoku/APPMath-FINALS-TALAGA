using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The heart of the renderer.
/// Owns all ComputeBuffers, dispatches the physics kernel every Update,
/// and issues two GPU-instanced draw calls per frame (tiles + entities).
/// Zero UnityEngine.Physics / Rigidbody / Collider.
/// </summary>
public class GPUInstanceRenderer : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("References")]
    public ComputeShader physicsCompute;
    public LevelData      levelData;
    public Mesh           quadMesh;          // unit quad   (-0.5 .. 0.5)
    public Material       tileMaterial;
    public Material       playerMaterial;

    [Header("Physics Tuning")]
    public float gravity      = 20f;
    public float jumpForce    = 9f;
    public float moveSpeed    = 7f;
    public float friction     = 18f;
    public float acceleration = 22f;
    public float maxFallSpeed = 30f;
    public float jumpBuffer   = 0.12f;
    public float coyoteTime   = 0.12f;

    // ─── GPU Buffers ──────────────────────────────────────────────────────────
    ComputeBuffer _entityBuffer;        // EntityState[]
    ComputeBuffer _entityMatrixBuffer;  // float4x4[] (written by compute)
    ComputeBuffer _tileBuffer;          // int[]       (solid/empty grid)
    ComputeBuffer _tileMatrixBuffer;    // float4x4[]  (written once)
    ComputeBuffer _inputBuffer;         // float4[]    (per-entity input)
    ComputeBuffer _tileArgsBuffer;      // DrawMeshInstancedIndirect args
    ComputeBuffer _entityArgsBuffer;

    // ─── Kernel IDs ───────────────────────────────────────────────────────────
    int _kernelPhysics;
    int _kernelTiles;

    // ─── Internal state ───────────────────────────────────────────────────────
    const int MAX_ENTITIES = 1;
    int _tileCount;
    RenderParams _tileRP;
    RenderParams _entityRP;

    /// <summary>Call every frame from PlayerInputReader.</summary>
    float2Input _pendingInput;

    public struct float2Input
    {
        public float moveX;
        public float jumpPressed; // 1 or 0
    }

    public void SetInput(float2Input input) => _pendingInput = input;

    // ─── Public read-back (for CameraFollow) ─────────────────────────────────
    // Sync read-back of player world position (tiny — just 1 entity)
    bool _ready = false;

    public Vector2 GetPlayerPosition()
    {
        if (!_ready || _entityBuffer == null) return Vector2.zero;
        EntityState[] tmp = new EntityState[1];
        _entityBuffer.GetData(tmp);
        return new Vector2(tmp[0].position.x, tmp[0].position.y);
    }

    // Must match the HLSL struct exactly (std430 layout)
    struct EntityState
    {
        public Vector2 position;     // 8 bytes (offset 0)
        public Vector2 velocity;     // 8 bytes (offset 8)
        public Vector2 size;         // 8 bytes (offset 16)
        public float   jumpBuffer;   // 4 bytes (offset 24)
        public int     onGround;     // 4 bytes (offset 28)
        public float   coyoteTime;   // 4 bytes (offset 32)
        public int     pad;          // 4 bytes (offset 36)
        public int     pad2;         // 4 bytes (offset 40)
        public int     pad3;         // 4 bytes (offset 44)
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────
    // NOTE: No Awake() here. BootstrapManager.Awake() (execution order -50)
    // assigns physicsCompute before our Start() runs, so FindKernel is safe.
    void Start()
    {
        if (physicsCompute == null)
        {
            Debug.LogError("[GPUInstanceRenderer] physicsCompute is null! "
                + "Make sure BootstrapManager has the compute shader assigned.");
            enabled = false;
            return;
        }
        _kernelPhysics = physicsCompute.FindKernel("CSPhysics");
        _kernelTiles   = physicsCompute.FindKernel("CSBuildTileMatrices");
        InitBuffers();
        BuildTileMatrices();
        SetupMaterials();
        _ready = true;

        // ── Debug readback: confirm compute shader wrote tile matrices ──────────
        // Check that at least one tile matrix is non-zero.
        // If all zeros → compute shader didn't run or kernel name is wrong.
        var matCheck = new Matrix4x4[levelData.width * levelData.height];
        _tileMatrixBuffer.GetData(matCheck);
        bool anyNonZero = false;
        for (int i = 0; i < matCheck.Length; i++)
        {
            if (matCheck[i] != Matrix4x4.zero) { anyNonZero = true; break; }
        }
        if (anyNonZero)
            Debug.Log("[GPUInstanceRenderer] ✓ Tile matrices written by GPU. First non-zero: " +
                matCheck[levelData.width].m03 + ", " + matCheck[levelData.width].m13);
        else
            Debug.LogError("[GPUInstanceRenderer] ✗ ALL tile matrices are ZERO. " +
                "CSBuildTileMatrices kernel did not write any data!");

        // Also confirm entity matrix after first physics dispatch
        var entCheck = new Matrix4x4[MAX_ENTITIES];
        _entityMatrixBuffer.GetData(entCheck);
        Debug.Log("[GPUInstanceRenderer] Entity[0] matrix pos = " +
            entCheck[0].m03 + ", " + entCheck[0].m13);
    }

    void Update()
    {
        if (!_ready) return;
        UploadInput();
        UploadConstants();
        DispatchPhysics();
        // Re-bind entity matrix buffer every frame so the shader always sees
        // the latest GPU-computed positions.
        Shader.SetGlobalBuffer("_EntityMatrixBuffer", _entityMatrixBuffer);
        DrawInstances();
    }

    void OnDestroy()
    {
        _entityBuffer?.Release();
        _entityMatrixBuffer?.Release();
        _tileBuffer?.Release();
        _tileMatrixBuffer?.Release();
        _inputBuffer?.Release();
        _tileArgsBuffer?.Release();
        _entityArgsBuffer?.Release();
    }

    // ─── Init ─────────────────────────────────────────────────────────────────
    void InitBuffers()
    {
        int ld = levelData.width * levelData.height;
        _tileCount = 0;
        foreach (var t in levelData.tiles) if (t == 1) _tileCount++;

        // Tile grid (int, 4 bytes each)
        _tileBuffer = new ComputeBuffer(ld, sizeof(int));
        _tileBuffer.SetData(levelData.tiles);

        // Tile matrices
        _tileMatrixBuffer = new ComputeBuffer(ld, 16 * sizeof(float));

        // Entity state (std430 float4 layout alignment = 48 bytes)
        // Match HLSL struct: float2 pos, float2 vel, float2 size, float jumpBuf, int onGround, float coyote, int pad (plus 8 bytes implicit padding)
        int entityStride = 48; 
        _entityBuffer = new ComputeBuffer(MAX_ENTITIES, entityStride);

        // Spawn player above spawn point
        EntityState spawn = new EntityState
        {
            position  = levelData.playerSpawn,
            velocity  = Vector2.zero,
            size      = new Vector2(0.38f, 0.48f),
            jumpBuffer = 0f,
            onGround  = 0,
            coyoteTime = 0f,
            pad       = 0,
            pad2      = 0,
            pad3      = 0
        };
        _entityBuffer.SetData(new[] { spawn });

        _entityMatrixBuffer = new ComputeBuffer(MAX_ENTITIES, 16 * sizeof(float));

        // Input: float4 (moveX, moveY intent, jumpPressed, unused)
        _inputBuffer = new ComputeBuffer(MAX_ENTITIES, 4 * sizeof(float));

        // Args buffers for DrawMeshInstancedIndirect
        // uint[5]: index count, instance count, start index, base vertex, start instance
        _tileArgsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        uint[] tileArgs = {
            quadMesh.GetIndexCount(0),
            (uint)(levelData.width * levelData.height), // draw ALL (empty → zero matrix)
            0, 0, 0
        };
        _tileArgsBuffer.SetData(tileArgs);

        _entityArgsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        uint[] entArgs = { quadMesh.GetIndexCount(0), (uint)MAX_ENTITIES, 0, 0, 0 };
        _entityArgsBuffer.SetData(entArgs);
    }

    void BuildTileMatrices()
    {
        int total = levelData.width * levelData.height;

        physicsCompute.SetBuffer(_kernelTiles, "TileBuffer",       _tileBuffer);
        physicsCompute.SetBuffer(_kernelTiles, "TileMatrixBuffer", _tileMatrixBuffer);
        physicsCompute.SetInt   ("GridWidth",  levelData.width);
        physicsCompute.SetInt   ("GridHeight", levelData.height);
        physicsCompute.SetFloat ("TileSize",   levelData.tileSize);
        physicsCompute.SetFloats("GridOrigin", levelData.gridOrigin.x, levelData.gridOrigin.y);

        int groups = Mathf.CeilToInt(total / 64f);
        physicsCompute.Dispatch(_kernelTiles, Mathf.Max(1, groups), 1, 1);
    }

    void SetupMaterials()
    {
        // Bind to BOTH material AND global so it works regardless of URP version.
        tileMaterial.SetBuffer  ("_TileMatrixBuffer",   _tileMatrixBuffer);
        playerMaterial.SetBuffer("_EntityMatrixBuffer", _entityMatrixBuffer);
        Shader.SetGlobalBuffer  ("_TileMatrixBuffer",   _tileMatrixBuffer);
        Shader.SetGlobalBuffer  ("_EntityMatrixBuffer", _entityMatrixBuffer);

        tileMaterial.enableInstancing   = true;
        playerMaterial.enableInstancing = true;
    }

    // ─── Per-frame ────────────────────────────────────────────────────────────
    void UploadInput()
    {
        float[] input = { _pendingInput.moveX, 0f, _pendingInput.jumpPressed, 0f };
        _inputBuffer.SetData(input);
    }

    void UploadConstants()
    {
        physicsCompute.SetFloat ("DeltaTime",    Time.deltaTime);
        physicsCompute.SetFloat ("Gravity",      gravity);
        physicsCompute.SetFloat ("JumpForce",    jumpForce);
        physicsCompute.SetFloat ("MoveSpeed",    moveSpeed);
        physicsCompute.SetFloat ("Friction",     friction);
        physicsCompute.SetFloat ("Acceleration", acceleration);
        physicsCompute.SetFloat ("MaxFallSpeed", maxFallSpeed);
        physicsCompute.SetFloat ("JumpBufferTime", jumpBuffer);
        physicsCompute.SetFloat ("CoyoteTimeDur",  coyoteTime);
        physicsCompute.SetInt   ("GridWidth",    levelData.width);
        physicsCompute.SetInt   ("GridHeight",   levelData.height);
        physicsCompute.SetFloat ("TileSize",     levelData.tileSize);
        physicsCompute.SetFloats("GridOrigin",   levelData.gridOrigin.x, levelData.gridOrigin.y);
    }

    void DispatchPhysics()
    {
        physicsCompute.SetBuffer(_kernelPhysics, "EntityBuffer",       _entityBuffer);
        physicsCompute.SetBuffer(_kernelPhysics, "EntityMatrixBuffer",  _entityMatrixBuffer);
        physicsCompute.SetBuffer(_kernelPhysics, "TileBuffer",          _tileBuffer);
        physicsCompute.SetBuffer(_kernelPhysics, "InputBuffer",         _inputBuffer);

        int groups = Mathf.CeilToInt(MAX_ENTITIES / 64f);
        physicsCompute.Dispatch(_kernelPhysics, Mathf.Max(1, groups), 1, 1);
    }

    void DrawInstances()
    {
        // Use a huge bounds so frustum culling NEVER kills this draw call.
        // Individual tile/entity visibility is handled by their zero-matrix (empty tiles = degenerate quad).
        Bounds worldBounds = new Bounds(Vector3.zero, Vector3.one * 1000f);

        Graphics.DrawMeshInstancedIndirect(
            quadMesh, 0, tileMaterial,
            worldBounds, _tileArgsBuffer,
            0, null, UnityEngine.Rendering.ShadowCastingMode.Off, false);

        // Player / entities
        Graphics.DrawMeshInstancedIndirect(
            quadMesh, 0, playerMaterial,
            worldBounds, _entityArgsBuffer,
            0, null, UnityEngine.Rendering.ShadowCastingMode.Off, false);
    }
}
