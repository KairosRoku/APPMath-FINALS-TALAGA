using UnityEngine;

/// <summary>
/// Stores the flat tile grid for a level.
/// 1 = solid tile, 0 = empty air.
/// Uploaded to the GPU once at startup.
/// </summary>
[CreateAssetMenu(fileName = "LevelData", menuName = "Platformer/LevelData")]
public class LevelData : ScriptableObject
{
    [Header("Grid dimensions")]
    public int width  = 20;
    public int height = 12;

    [Header("World scale")]
    public float tileSize = 1f;

    /// <summary>
    /// World-space position of the bottom-left tile (0,0) center.
    /// </summary>
    public Vector2 gridOrigin = Vector2.zero;

    [Header("Tile data (width × height, row-major, y=0 is bottom)")]
    [Tooltip("Length must equal width * height. 1=solid, 0=air.")]
    public int[] tiles;

    /// <summary>
    /// Player spawn position in world space.
    /// </summary>
    public Vector2 playerSpawn = new Vector2(2f, 3f);

    /// <summary>
    /// Fills tiles with a default level if empty, useful on first creation.
    /// </summary>
    private void OnValidate()
    {
        int expected = width * height;
        if (tiles == null || tiles.Length != expected)
        {
            tiles = BuildDefaultLevel(width, height);
        }
    }

    public static int[] BuildDefaultLevel(int w, int h)
    {
        int[] t = new int[w * h];

        // Solid floor (row 0)
        for (int x = 0; x < w; x++) t[0 * w + x] = 1;

        // Solid left & right walls
        for (int y = 0; y < h; y++)
        {
            t[y * w + 0]     = 1;
            t[y * w + (w-1)] = 1;
        }

        // Solid ceiling
        for (int x = 0; x < w; x++) t[(h-1) * w + x] = 1;

        // Platforms
        // Platform 1: y=3, x=3..7
        for (int x = 3; x <= 7; x++)  t[3 * w + x] = 1;

        // Platform 2: y=5, x=10..14
        for (int x = 10; x <= 14; x++) t[5 * w + x] = 1;

        // Platform 3: y=7, x=5..9
        for (int x = 5; x <= 9; x++)  t[7 * w + x] = 1;

        // Platform 4: y=9, x=12..17
        for (int x = 12; x <= 17; x++) t[9 * w + x] = 1;

        return t;
    }
}
