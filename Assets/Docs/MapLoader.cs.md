```C#
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class MapLoader : MonoBehaviour
{
    [Header("Map Source (.map in Assets/Mapdata)")]
    [Tooltip("File name of the .map file inside Assets/Mapdata, e.g. 'random-32-32-10.map'.")]
    public string mapFileName = "random-32-32-10.map";

    [Header("Tile Settings")]
    [Tooltip("World size of one tile in X/Z.")]
    public float tileSize = 1f;

    [Tooltip("Use XY plane (2D) instead of XZ (3D).")]
    public bool useXYPlane = true;

    [Tooltip("Parent transform for all generated tiles. If null, tiles will be created under this GameObject.")]
    public Transform tilesParent;

    [Header("Optional Build Window (0 = full map)")]
    [Tooltip("Maximum map width to build. 0 means build full width.")]
    [Min(0)]
    public int maxBuildWidth = 0;

    [Tooltip("Maximum map height to build. 0 means build full height.")]
    [Min(0)]
    public int maxBuildHeight = 0;

    [Tooltip("Map X offset for the top-left of the build window.")]
    [Min(0)]
    public int mapOffsetX = 0;

    [Tooltip("Map Y offset for the top-left of the build window.")]
    [Min(0)]
    public int mapOffsetY = 0;

    [Header("Pathfinding Rules")]
    [Tooltip("Characters treated as walkable by IsWalkable. Default '.' only.")]
    public string walkableCells = ".";

    private readonly Dictionary<char, Color> _colorByChar = new Dictionary<char, Color>
    {
        { '@', new Color(50f / 255f, 50f / 255f, 50f / 255f) },       // Obstacle - xám đậm
        { 'T', new Color(34f / 255f, 139f / 255f, 34f / 255f) },      // Tree - xanh lá
        { '.', new Color(240f / 255f, 240f / 255f, 240f / 255f) },    // Walkable - trắng xám nhạt
        { 'S', new Color(100f / 255f, 100f / 255f, 200f / 255f) },    // Swamp - xanh tím
        { 'W', new Color(70f / 255f, 130f / 255f, 180f / 255f) },     // Water - xanh nước biển
    };

    private char[][] _grid;
    private int _width;
    private int _height;
    private int _buildStartX;
    private int _buildStartY;
    private int _buildWidth;
    private int _buildHeight;

    public int Width => _width;
    public int Height => _height;
    public char[][] Grid => _grid;
    public int BuildStartX => _buildStartX;
    public int BuildStartY => _buildStartY;
    public int BuildWidth => _buildWidth;
    public int BuildHeight => _buildHeight;

    private void Reset()
    {
        tilesParent = transform;
    }

    [ContextMenu("Load Map Now")]
    public void LoadMapContextMenu()
    {
        LoadAndBuild();
    }

    private void Start()
    {
        LoadAndBuild();
    }

    public void LoadAndBuild()
    {
        if (string.IsNullOrWhiteSpace(mapFileName))
        {
            Debug.LogError("[MapLoader] mapFileName is empty.");
            return;
        }

        if (tilesParent == null)
        {
            tilesParent = transform;
        }

        ClearExistingTiles();

        string mapPath = Path.Combine(Application.dataPath, "Mapdata", mapFileName);

        if (!File.Exists(mapPath))
        {
            Debug.LogError($"[MapLoader] Map file not found at path: {mapPath}");
            return;
        }

        try
        {
            _grid = ReadMapFile(mapPath, out _width, out _height);
            ComputeBuildWindow();
            BuildTiles(_grid);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MapLoader] Failed to load map: {ex.Message}");
        }
    }

    private void ClearExistingTiles()
    {
        if (tilesParent == null) return;

        List<Transform> children = new List<Transform>();
        foreach (Transform child in tilesParent)
        {
            children.Add(child);
        }

        for (int i = children.Count - 1; i >= 0; i--)
        {
            if (Application.isPlaying)
            {
                Destroy(children[i].gameObject);
            }
            else
            {
                DestroyImmediate(children[i].gameObject);
            }
        }
    }

    private char[][] ReadMapFile(string filepath, out int width, out int height)
    {
        string[] lines = File.ReadAllLines(filepath);

        if (lines.Length < 4)
        {
            throw new Exception($"File map không hợp lệ: {filepath}");
        }

        // Example header:
        // type octile
        // height 257
        // width 256
        string[] heightParts = lines[1].Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        string[] widthParts = lines[2].Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (heightParts.Length < 2 || widthParts.Length < 2)
        {
            throw new Exception("Định dạng header map không đúng.");
        }

        height = int.Parse(heightParts[1]);
        width = int.Parse(widthParts[1]);

        if (!lines[3].Trim().StartsWith("map", StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception("Định dạng map không đúng, dòng thứ 4 phải là 'map'.");
        }

        List<char[]> rows = new List<char[]>(height);
        for (int i = 4; i < 4 + height && i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r', '\n');
            char[] row = line.ToCharArray();

            if (row.Length < width)
            {
                char[] padded = new char[width];
                Array.Copy(row, padded, row.Length);
                for (int x = row.Length; x < width; x++)
                {
                    padded[x] = '@';
                }
                row = padded;
            }
            else if (row.Length > width)
            {
                char[] cut = new char[width];
                Array.Copy(row, cut, width);
                row = cut;
            }

            rows.Add(row);
        }

        while (rows.Count < height)
        {
            char[] filler = new char[width];
            for (int x = 0; x < width; x++)
            {
                filler[x] = '@';
            }
            rows.Add(filler);
        }

        return rows.ToArray();
    }

    private void ComputeBuildWindow()
    {
        if (_width <= 0 || _height <= 0)
        {
            _buildStartX = 0;
            _buildStartY = 0;
            _buildWidth = 0;
            _buildHeight = 0;
            return;
        }

        _buildStartX = Mathf.Clamp(mapOffsetX, 0, _width - 1);
        _buildStartY = Mathf.Clamp(mapOffsetY, 0, _height - 1);

        int availableWidth = _width - _buildStartX;
        int availableHeight = _height - _buildStartY;

        _buildWidth = maxBuildWidth > 0 ? Mathf.Min(maxBuildWidth, availableWidth) : availableWidth;
        _buildHeight = maxBuildHeight > 0 ? Mathf.Min(maxBuildHeight, availableHeight) : availableHeight;
    }

    private void BuildTiles(char[][] grid)
    {
        if (grid == null || grid.Length == 0) return;
        if (_buildWidth <= 0 || _buildHeight <= 0) return;

        for (int localY = 0; localY < _buildHeight; localY++)
        {
            int mapY = _buildStartY + localY;
            char[] row = grid[mapY];
            for (int localX = 0; localX < _buildWidth; localX++)
            {
                int mapX = _buildStartX + localX;
                char cell = row[mapX];

                if (!_colorByChar.ContainsKey(cell))
                {
                    continue;
                }

                Vector3 position = CellToWorld(new Vector2Int(mapX, mapY));
                GameObject tile = new GameObject($"{cell}_({mapX},{mapY})");
                tile.transform.SetParent(tilesParent, false);
                tile.transform.position = position;

                ApplyColorIfNeeded(tile, cell);
                ApplyScaleIfNeeded(tile);
                ApplyColliderIfNeeded(tile, cell);
            }
        }
    }

    public bool IsWalkable(Vector2Int cell)
    {
        if (!IsInside(cell))
        {
            return false;
        }

        return IsCellTypeWalkable(_grid[cell.y][cell.x]);
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        int localX = cell.x - _buildStartX;
        int localY = cell.y - _buildStartY;
        float totalWidth = _buildWidth * tileSize;
        float totalHeight = _buildHeight * tileSize;

        if (useXYPlane)
        {
            Vector2 origin2D = new Vector2(-totalWidth / 2f + tileSize / 2f, totalHeight / 2f - tileSize / 2f);
            Vector2 pos2D = origin2D + new Vector2(localX * tileSize, -localY * tileSize);
            return new Vector3(pos2D.x, pos2D.y, 0f);
        }

        Vector3 origin = new Vector3(-totalWidth / 2f + tileSize / 2f, 0f, -totalHeight / 2f + tileSize / 2f);
        return origin + new Vector3(localX * tileSize, 0f, localY * tileSize);
    }

    public Vector2Int WorldToCell(Vector3 position)
    {
        if (tileSize <= Mathf.Epsilon)
        {
            return new Vector2Int(_buildStartX, _buildStartY);
        }

        float totalWidth = _buildWidth * tileSize;
        float totalHeight = _buildHeight * tileSize;

        int localX;
        int localY;

        if (useXYPlane)
        {
            Vector2 origin2D = new Vector2(-totalWidth / 2f + tileSize / 2f, totalHeight / 2f - tileSize / 2f);
            localX = Mathf.RoundToInt((position.x - origin2D.x) / tileSize);
            localY = Mathf.RoundToInt((origin2D.y - position.y) / tileSize);
        }
        else
        {
            Vector3 origin = new Vector3(-totalWidth / 2f + tileSize / 2f, 0f, -totalHeight / 2f + tileSize / 2f);
            localX = Mathf.RoundToInt((position.x - origin.x) / tileSize);
            localY = Mathf.RoundToInt((position.z - origin.z) / tileSize);
        }

        return new Vector2Int(localX + _buildStartX, localY + _buildStartY);
    }

    public bool IsInside(Vector2Int cell)
    {
        return _grid != null &&
               cell.x >= 0 &&
               cell.x < _width &&
               cell.y >= 0 &&
               cell.y < _height;
    }

    private bool IsCellTypeWalkable(char cellType)
    {
        string walkable = string.IsNullOrEmpty(walkableCells) ? "." : walkableCells;
        return walkable.IndexOf(cellType) >= 0;
    }

    private void ApplyColorIfNeeded(GameObject tile, char cell)
    {
        if (!_colorByChar.TryGetValue(cell, out Color color))
        {
            return;
        }

        RuntimeSpriteVisuals.Disable3DRenderers(tile);
        RuntimeSpriteVisuals.EnsureSpriteRenderer(tile, color, RuntimeSpriteVisuals.TileSortingOrder);
    }

    private void ApplyScaleIfNeeded(GameObject tile)
    {
        tile.transform.localScale = new Vector3(tileSize, tileSize, 1f);
    }

    private void ApplyColliderIfNeeded(GameObject tile, char cell)
    {
        if (IsCellTypeWalkable(cell))
        {
            BoxCollider2D walkableCollider = tile.GetComponent<BoxCollider2D>();
            if (walkableCollider != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(walkableCollider);
                }
                else
                {
                    DestroyImmediate(walkableCollider);
                }
            }

            return;
        }

        GridPhysics2DUtility.EnsureBoxCollider2D(tile, Vector2.one * Mathf.Max(0.01f, tileSize), false);
    }
}

public static class RuntimeSpriteVisuals
{
    public const int TileSortingOrder = 0;
    public const int PathSortingOrder = 20;
    public const int CollisionSortingOrder = 30;
    public const int ObjectiveSortingOrder = 40;
    public const int AgentSortingOrder = 50;
    public const int ProjectileSortingOrder = 60;

    private static Sprite _whiteSprite;
    private static Texture2D _whiteTexture;

    public static Sprite GetWhiteSprite()
    {
        if (_whiteSprite != null)
        {
            return _whiteSprite;
        }

        _whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        _whiteTexture.name = "RuntimeWhiteSpriteTexture";
        _whiteTexture.SetPixel(0, 0, Color.white);
        _whiteTexture.Apply(false, true);

        _whiteSprite = Sprite.Create(_whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        _whiteSprite.name = "RuntimeWhiteSprite";
        return _whiteSprite;
    }

    public static SpriteRenderer EnsureSpriteRenderer(GameObject obj, Color color, int sortingOrder)
    {
        SpriteRenderer spriteRenderer = obj.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = obj.AddComponent<SpriteRenderer>();
        }

        if (spriteRenderer.sprite == null)
        {
            spriteRenderer.sprite = GetWhiteSprite();
        }

        spriteRenderer.color = color;
        spriteRenderer.sortingLayerName = "Default";
        spriteRenderer.sortingOrder = sortingOrder;
        spriteRenderer.enabled = true;
        return spriteRenderer;
    }

    public static bool HasAssignedSpriteRendererInChildren(GameObject obj, bool includeRoot)
    {
        SpriteRenderer[] spriteRenderers = obj.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (!includeRoot && spriteRenderers[i].gameObject == obj)
            {
                continue;
            }

            if (spriteRenderers[i].sprite != null)
            {
                return true;
            }
        }

        return false;
    }

    public static void ApplySortingOrderToSprites(GameObject obj, int sortingOrder)
    {
        SpriteRenderer[] spriteRenderers = obj.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i].sprite == null)
            {
                continue;
            }

            spriteRenderers[i].sortingLayerName = "Default";
            spriteRenderers[i].sortingOrder = sortingOrder;
            spriteRenderers[i].enabled = true;
        }
    }

    public static void ApplyColorToChildSprites(GameObject obj, Color color, int sortingOrder)
    {
        SpriteRenderer[] spriteRenderers = obj.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i].gameObject == obj || spriteRenderers[i].sprite == null)
            {
                continue;
            }

            spriteRenderers[i].color = color;
            spriteRenderers[i].sortingLayerName = "Default";
            spriteRenderers[i].sortingOrder = sortingOrder;
            spriteRenderers[i].enabled = true;
        }
    }

    public static void DisableRootSpriteRenderer(GameObject obj)
    {
        SpriteRenderer spriteRenderer = obj.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = false;
        }
    }

    public static void Disable3DRenderers(GameObject obj)
    {
        MeshRenderer[] meshRenderers = obj.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < meshRenderers.Length; i++)
        {
            meshRenderers[i].enabled = false;
        }

        SkinnedMeshRenderer[] skinnedMeshRenderers = obj.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinnedMeshRenderers.Length; i++)
        {
            skinnedMeshRenderers[i].enabled = false;
        }
    }
}
```
