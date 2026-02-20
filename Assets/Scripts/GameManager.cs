using UnityEngine;

/// <summary>
/// Self-contained game loop.
/// 
/// RENDERING: Graphics.DrawMeshInstanced — true GPU instancing, no GameObjects.
/// PHYSICS:   Pure C# math — AABB vs tilemap, gravity, coyote time, jump buffer.
///            Zero Rigidbody, zero Collider, zero Unity Physics.
/// </summary>
public class GameManager : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────────
    [Header("Level")]
    public LevelData levelData;

    [Header("Materials")]
    public Material tileMaterial;
    public Material playerMaterial;

    [Header("Physics Tuning")]
    public float gravity       = 22f;
    public float jumpForce     = 10f;
    public float moveSpeed     = 7f;
    public float acceleration  = 30f;
    public float friction      = 28f;
    public float maxFallSpeed  = 30f;
    public float coyoteTime    = 0.12f;
    public float jumpBufferTime = 0.12f;

    [Header("Player Size (half-extents)")]
    public Vector2 playerHalfSize = new Vector2(0.34f, 0.44f);

    // ── State ──────────────────────────────────────────────────────────────────
    Vector2 _pos;
    Vector2 _vel;
    bool    _onGround;
    float   _coyoteTimer;
    float   _jumpBuffer;

    // ── GPU Instancing buffers ─────────────────────────────────────────────────
    Matrix4x4[] _tileMatrices;   // static, built once
    Matrix4x4[] _playerMatrix = new Matrix4x4[1];

    Mesh _quad;

    // Public for CameraFollow
    public Vector2 PlayerPosition => _pos;

    // ── Unity ──────────────────────────────────────────────────────────────────
    void Start()
    {
        _quad = BuildQuad();
        BuildTileMatrices();
        _pos = levelData.playerSpawn;
        _vel = Vector2.zero;
    }

    void Update()
    {
        // ── Input ──────────────────────────────────────────────────────────────
        float moveX = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))  moveX -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) moveX += 1f;
        bool jumpPressed = Input.GetKeyDown(KeyCode.Space)
                        || Input.GetKeyDown(KeyCode.W)
                        || Input.GetKeyDown(KeyCode.UpArrow);

        // ── Physics step ───────────────────────────────────────────────────────
        StepPhysics(moveX, jumpPressed, Time.deltaTime);

        // ── Build player matrix ────────────────────────────────────────────────
        _playerMatrix[0] = Matrix4x4.TRS(
            new Vector3(_pos.x, _pos.y, 0f),
            Quaternion.identity,
            new Vector3(playerHalfSize.x * 2f, playerHalfSize.y * 2f, 1f));

        // ── GPU-instanced draw calls (zero GameObjects involved) ───────────────
        if (_tileMatrices.Length > 0)
            Graphics.DrawMeshInstanced(_quad, 0, tileMaterial, _tileMatrices);

        Graphics.DrawMeshInstanced(_quad, 0, playerMaterial, _playerMatrix);
    }

    // ── Pure math physics ──────────────────────────────────────────────────────
    void StepPhysics(float moveX, bool jumpPressed, float dt)
    {
        // Horizontal velocity
        float targetVX = moveX * moveSpeed;
        if (Mathf.Abs(moveX) > 0.01f)
            _vel.x = Mathf.MoveTowards(_vel.x, targetVX, acceleration * dt);
        else
            _vel.x = Mathf.MoveTowards(_vel.x, 0f, friction * dt);

        // Gravity
        if (!_onGround) _vel.y -= gravity * dt;
        _vel.y = Mathf.Max(_vel.y, -maxFallSpeed);

        // Jump buffer
        if (jumpPressed) _jumpBuffer = jumpBufferTime;
        else             _jumpBuffer = Mathf.Max(0f, _jumpBuffer - dt);

        // Coyote time
        if (_onGround) _coyoteTimer = coyoteTime;
        else           _coyoteTimer = Mathf.Max(0f, _coyoteTimer - dt);

        // Execute jump
        if (_jumpBuffer > 0f && _coyoteTimer > 0f)
        {
            _vel.y        = jumpForce;
            _jumpBuffer   = 0f;
            _coyoteTimer  = 0f;
            _onGround     = false;
        }

        // Integrate
        _pos += _vel * dt;

        // AABB collision resolution
        _onGround = false;
        ResolveCollisions();
    }

    void ResolveCollisions()
    {
        float hw   = playerHalfSize.x;
        float hh   = playerHalfSize.y;
        float skin = 0.04f; // inset for diagonal-tile false positives

        // ── Floor / ceiling ──────────────────────────────────────────────────
        // Feet
        if (TileSolidWorld(_pos + new Vector2(-hw + skin, -hh)) ||
            TileSolidWorld(_pos + new Vector2( hw - skin, -hh)))
        {
            int ty = WorldToTileY(_pos.y - hh);
            _pos.y    = TileCenterY(ty) + levelData.tileSize * 0.5f + hh;
            _vel.y    = Mathf.Max(0f, _vel.y);
            _onGround = true;
        }

        // Head
        if (TileSolidWorld(_pos + new Vector2(-hw + skin, hh)) ||
            TileSolidWorld(_pos + new Vector2( hw - skin, hh)))
        {
            int ty = WorldToTileY(_pos.y + hh);
            _pos.y = TileCenterY(ty) - levelData.tileSize * 0.5f - hh;
            _vel.y = Mathf.Min(0f, _vel.y);
        }

        // ── Left / right walls ───────────────────────────────────────────────
        // Right
        if (TileSolidWorld(_pos + new Vector2(hw, -hh + skin)) ||
            TileSolidWorld(_pos + new Vector2(hw,  hh - skin)))
        {
            int tx = WorldToTileX(_pos.x + hw);
            _pos.x = TileCenterX(tx) - levelData.tileSize * 0.5f - hw;
            _vel.x = Mathf.Min(0f, _vel.x);
        }

        // Left
        if (TileSolidWorld(_pos + new Vector2(-hw, -hh + skin)) ||
            TileSolidWorld(_pos + new Vector2(-hw,  hh - skin)))
        {
            int tx = WorldToTileX(_pos.x - hw);
            _pos.x = TileCenterX(tx) + levelData.tileSize * 0.5f + hw;
            _vel.x = Mathf.Max(0f, _vel.x);
        }
    }

    // ── Tile helpers ───────────────────────────────────────────────────────────
    bool TileSolidWorld(Vector2 worldPos)
    {
        int tx = WorldToTileX(worldPos.x);
        int ty = WorldToTileY(worldPos.y);
        if (tx < 0 || tx >= levelData.width || ty < 0 || ty >= levelData.height)
            return false;
        return levelData.tiles[ty * levelData.width + tx] == 1;
    }

    int WorldToTileX(float x)
    {
        return Mathf.FloorToInt((x - levelData.gridOrigin.x) / levelData.tileSize);
    }

    int WorldToTileY(float y)
    {
        return Mathf.FloorToInt((y - levelData.gridOrigin.y) / levelData.tileSize);
    }

    float TileCenterX(int tx) =>
        levelData.gridOrigin.x + (tx + 0.5f) * levelData.tileSize;

    float TileCenterY(int ty) =>
        levelData.gridOrigin.y + (ty + 0.5f) * levelData.tileSize;

    // ── Graphics setup ─────────────────────────────────────────────────────────
    void BuildTileMatrices()
    {
        // Count solid tiles (DrawMeshInstanced needs exactly the right count)
        int solidCount = 0;
        foreach (int t in levelData.tiles) if (t == 1) solidCount++;

        _tileMatrices = new Matrix4x4[solidCount];
        int idx = 0;
        float ts = levelData.tileSize;

        for (int y = 0; y < levelData.height; y++)
        for (int x = 0; x < levelData.width;  x++)
        {
            if (levelData.tiles[y * levelData.width + x] != 1) continue;

            Vector3 center = new Vector3(
                levelData.gridOrigin.x + (x + 0.5f) * ts,
                levelData.gridOrigin.y + (y + 0.5f) * ts,
                0f);

            _tileMatrices[idx++] = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one * ts);
        }

        Debug.Log($"[GameManager] Built {solidCount} solid tile matrices.");
    }

    static Mesh BuildQuad()
    {
        var mesh = new Mesh { name = "ProceduralQuad" };
        mesh.vertices  = new[] {
            new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f),
            new Vector3(0.5f,  0.5f),  new Vector3(-0.5f, 0.5f)
        };
        mesh.uv        = new[] {
            new Vector2(0,0), new Vector2(1,0),
            new Vector2(1,1), new Vector2(0,1)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
