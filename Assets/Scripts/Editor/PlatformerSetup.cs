using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.IO;

/// <summary>
/// One-click full scene setup for the GPU Instanced Platformer.
/// Run: Tools → Platformer → BUILD FULL SCENE
/// This creates all assets AND sets up every GameObject/component/reference.
/// </summary>
public static class PlatformerSetup
{
    const string LEVELS_PATH    = "Assets/Levels";
    const string MATERIALS_PATH = "Assets/Materials";
    const string SCRIPTS_PATH   = "Assets/Scripts";
    const string SHADERS_PATH   = "Assets/Shaders";

    // ─────────────────────────────────────────────────────────────────────────
    // MAIN ENTRY POINT
    // ─────────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/Platformer/★ BUILD FULL SCENE ★")]
    static void BuildFullScene()
    {
        // 1. Ensure all asset folders exist
        EnsureDir(LEVELS_PATH);
        EnsureDir(MATERIALS_PATH);

        // 2. Create / load LevelData SO
        LevelData levelData = GetOrCreateLevelData();

        // 3. Create / load Materials
        Material tileMat   = GetOrCreateMaterial("TileMaterial",   "Platformer/TileInstanced");
        Material playerMat = GetOrCreateMaterial("PlayerMaterial",  "Platformer/PlayerInstanced");


        // 4. Find Compute Shader
        ComputeShader physCS = FindComputeShader("PhysicsCompute");
        if (physCS == null) Debug.LogWarning("[PlatformerSetup] PhysicsCompute shader not found! Assign manually on Bootstrap.");

        // 5. Clear existing scene objects
        ClearScene();

        // 6. Camera
        GameObject camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        Camera cam = camGO.AddComponent<Camera>();
        cam.orthographic     = true;
        cam.orthographicSize = 6f;
        cam.clearFlags       = CameraClearFlags.SolidColor;
        cam.backgroundColor  = new Color(0.05f, 0.07f, 0.12f, 1f);
        cam.nearClipPlane    = 0.3f;
        cam.farClipPlane     = 1000f;
        cam.depth            = -1;
        camGO.transform.position = new Vector3(
            levelData.gridOrigin.x + levelData.width  * levelData.tileSize * 0.5f,
            levelData.gridOrigin.y + levelData.height * levelData.tileSize * 0.5f,
            -10f);

        CameraFollow camFollow = camGO.AddComponent<CameraFollow>();
        camFollow.smoothSpeed  = 6f;
        camFollow.offset       = new Vector2(0f, 1.2f);
        camFollow.clampToLevel = true;

        // 7. Bootstrap + GPU Renderer
        GameObject bootstrapGO = new GameObject("Bootstrap");
        GPUInstanceRenderer gpuRenderer = bootstrapGO.AddComponent<GPUInstanceRenderer>();
        PlayerInputReader inputReader = bootstrapGO.AddComponent<PlayerInputReader>();
        BootstrapManager bootstrap = bootstrapGO.AddComponent<BootstrapManager>();

        bootstrap.gpuRenderer = gpuRenderer;
        bootstrap.cameraFollow = camFollow;
        bootstrap.levelData = levelData;
        bootstrap.tileMaterial = tileMat;
        bootstrap.playerMaterial = playerMat;
        bootstrap.physicsCompute = physCS;

        // 8. Wire camera to renderer (Optional, as BootstrapManager does it on Awake, but good for editor state)
        camFollow.gpuRenderer = gpuRenderer;

        // 9. Save scene
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        Selection.activeGameObject = bootstrapGO;
        EditorGUIUtility.PingObject(bootstrapGO);

        Debug.Log("[PlatformerSetup] ★ Scene built! Press Play.");
        EditorUtility.DisplayDialog("Done!",
            "Scene built!\n\n" +
            "Hierarchy:\n" +
            "  ├─ Main Camera  (CameraFollow)\n" +
            "  └─ Bootstrap    (BootstrapManager, GPUInstanceRenderer, PlayerInputReader)\n\n" +
            "Press ▶ PLAY\n\n" +
            "Controls: A/D or ←/→ to move, Space/W/↑ to jump.",
            "Play! ▶");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DEBUG TOOLS
    // ─────────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/Platformer/Print Level ASCII")]
    static void PrintLevel()
    {
        var ld = AssetDatabase.LoadAssetAtPath<LevelData>($"{LEVELS_PATH}/Level01.asset");
        if (ld == null) { Debug.LogWarning("No Level01 found. Run BUILD FULL SCENE first."); return; }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Level01]  {ld.width}×{ld.height}  tileSize={ld.tileSize}");
        for (int y = ld.height - 1; y >= 0; y--)
        {
            for (int x = 0; x < ld.width; x++)
                sb.Append(ld.tiles[y * ld.width + x] == 1 ? "█" : "·");
            sb.AppendLine();
        }
        Debug.Log(sb.ToString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────
    static LevelData GetOrCreateLevelData()
    {
        string path = $"{LEVELS_PATH}/Level01.asset";
        var ld = AssetDatabase.LoadAssetAtPath<LevelData>(path);
        if (ld != null) return ld;

        ld = ScriptableObject.CreateInstance<LevelData>();
        ld.width       = 20;
        ld.height      = 12;
        ld.tileSize    = 1f;
        ld.gridOrigin  = Vector2.zero;
        ld.playerSpawn = new Vector2(2f, 2f);
        ld.tiles       = LevelData.BuildDefaultLevel(ld.width, ld.height);
        AssetDatabase.CreateAsset(ld, path);
        AssetDatabase.SaveAssets();
        Debug.Log($"[PlatformerSetup] Created {path}");
        return ld;
    }

    static Material GetOrCreateMaterial(string name, string shaderName)
    {
        string path = $"{MATERIALS_PATH}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        Shader sh = Shader.Find(shaderName);
        if (sh == null)
        {
            Debug.LogWarning($"[PlatformerSetup] Shader '{shaderName}' not found — using URP/Unlit fallback.");
            sh = Shader.Find("Universal Render Pipeline/Unlit");
        }
        mat = new Material(sh) { name = name };
        AssetDatabase.CreateAsset(mat, path);
        AssetDatabase.SaveAssets();
        Debug.Log($"[PlatformerSetup] Created {path}");
        return mat;
    }

    static ComputeShader FindComputeShader(string name)
    {
        // Search all compute shaders in the project
        string[] guids = AssetDatabase.FindAssets($"{name} t:ComputeShader");
        if (guids.Length == 0) return null;
        return AssetDatabase.LoadAssetAtPath<ComputeShader>(
            AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    static void ClearScene()
    {
        // Remove only our specific objects to avoid nuking other things
        string[] names = { "Main Camera", "Bootstrap", "Directional Light" };
        foreach (string n in names)
        {
            var go = GameObject.Find(n);
            if (go != null)
            {
                Object.DestroyImmediate(go);
                Debug.Log($"[PlatformerSetup] Removed old '{n}'");
            }
        }
    }

    static void EnsureDir(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string folder = Path.GetFileName(path);
        AssetDatabase.CreateFolder(parent, folder);
    }
}
