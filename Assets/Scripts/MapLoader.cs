using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class MapLoader : MonoBehaviour
{
    [Header("Map Source")]
    public string mapFileName = "random-32-32-10.map";
    public bool useSelectedMapFileOverride = true;
    public float tileSize = 1f;
    public bool buildOnStart = false;
    public Transform tilesParent;

    [Header("Build Window")]
    [Min(0)] public int maxBuildWidth = 0;
    [Min(0)] public int maxBuildHeight = 0;
    [Min(0)] public int mapOffsetX = 0;
    [Min(0)] public int mapOffsetY = 0;

    [Header("Rules")]
    public string walkableCells = ".";

    [Header("Sprite Assets")]
    public Sprite groundSprite;
    public Sprite alternateGroundSprite;
    public Sprite obstacleSprite;
    public Sprite treeSprite;
    public Sprite waterSprite;
    public Sprite swampSprite;

    [Header("Editor Asset Paths")]
    public string groundSpritePath = "Assets/Sprites/Kenny Topdown Tanks Redux/PNG/Retina/tileGrass1.png";
    public string alternateGroundSpritePath = "Assets/Sprites/Kenny Topdown Tanks Redux/PNG/Retina/tileGrass2.png";
    public string obstacleSpritePath = "Assets/Sprites/Kenny Topdown Tanks Redux/PNG/Retina/crateMetal.png";
    public string treeSpritePath = "Assets/Sprites/Kenny Topdown Tanks Redux/PNG/Retina/treeGreen_large.png";

    private char[][] grid;
    private int width;
    private int height;
    private int buildStartX;
    private int buildStartY;
    private int buildWidth;
    private int buildHeight;
    private Sprite fallbackSprite;
    private readonly HashSet<Vector2Int> _destructibleCells = new HashSet<Vector2Int>();
    private const string MovementObstacleLayerName = "Walls";
    private const string LegacyMovementObstacleLayerName = "ObstaclesMovement";
    private const string BulletObstacleLayerName = "Hittable";

    public int Width => width;
    public int Height => height;
    public int BuildStartX => buildStartX;
    public int BuildStartY => buildStartY;
    public int BuildWidth => buildWidth;
    public int BuildHeight => buildHeight;

    private void Reset()
    {
        tilesParent = transform;
    }

    private void Start()
    {
        if (buildOnStart)
        {
            LoadAndBuild();
        }
    }

    [ContextMenu("Load Map Now")]
    public void LoadAndBuild()
    {
        if (tilesParent == null)
        {
            tilesParent = transform;
        }

        ClearExistingTiles();
        LoadDefaultSprites();

        string overrideFile = useSelectedMapFileOverride
            ? UnityEngine.PlayerPrefs.GetString("SelectedMapFile", string.Empty)
            : string.Empty;
        if (!string.IsNullOrEmpty(overrideFile)) mapFileName = Path.GetFileName(overrideFile);

        string mapPath = Path.Combine(Application.streamingAssetsPath, "MapData", mapFileName);
        if (!File.Exists(mapPath))
            mapPath = Path.Combine(Application.dataPath, "MapData", mapFileName);
        if (!File.Exists(mapPath))
        {
            Debug.LogError($"[MapLoader] Map file not found: {mapPath}");
            return;
        }

        grid = ReadMapFile(mapPath, out width, out height);
        ComputeBuildWindow();
        BuildTiles();
        CreateMapBounds();
        StaticBatchingUtility.Combine(tilesParent.gameObject);

        FitCamera();
    }

    public bool IsWalkable(Vector2Int cell)
    {
        return IsInside(cell) && IsCellWalkable(grid[cell.y][cell.x]);
    }

    public bool IsInside(Vector2Int cell)
    {
        return grid != null && cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        float totalWidth = buildWidth * tileSize;
        float totalHeight = buildHeight * tileSize;
        float localX = cell.x - buildStartX;
        float localY = cell.y - buildStartY;
        Vector2 origin = new Vector2(-totalWidth / 2f + tileSize / 2f, totalHeight / 2f - tileSize / 2f);
        Vector2 position = origin + new Vector2(localX * tileSize, -localY * tileSize);
        return new Vector3(position.x, position.y, 0f);
    }

    public Vector2Int WorldToCell(Vector3 position)
    {
        float totalWidth = buildWidth * tileSize;
        float totalHeight = buildHeight * tileSize;
        Vector2 origin = new Vector2(-totalWidth / 2f + tileSize / 2f, totalHeight / 2f - tileSize / 2f);
        int localX = Mathf.RoundToInt((position.x - origin.x) / tileSize);
        int localY = Mathf.RoundToInt((origin.y - position.y) / tileSize);
        return new Vector2Int(localX + buildStartX, localY + buildStartY);
    }

    public void MarkCellBlocked(Vector2Int cell)
    {
        if (grid != null && cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height)
            grid[cell.y][cell.x] = '@';
    }

    public void MarkCellDestructible(Vector2Int cell)
    {
        _destructibleCells.Add(cell);
        MarkCellBlocked(cell);
    }

    public bool IsDestructibleBlocked(Vector2Int cell) => _destructibleCells.Contains(cell);

    public void UnmarkCellBlocked(Vector2Int cell)
    {
        _destructibleCells.Remove(cell);
        if (grid != null && cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height)
            grid[cell.y][cell.x] = '.';
    }

    public bool TryFindWalkableNear(Vector2Int preferredCell, out Vector2Int result)
    {
        if (IsWalkable(preferredCell))
        {
            result = preferredCell;
            return true;
        }

        int maxRadius = Mathf.Max(width, height);
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            for (int y = preferredCell.y - radius; y <= preferredCell.y + radius; y++)
            {
                for (int x = preferredCell.x - radius; x <= preferredCell.x + radius; x++)
                {
                    Vector2Int candidate = new Vector2Int(x, y);
                    if (IsWalkable(candidate))
                    {
                        result = candidate;
                        return true;
                    }
                }
            }
        }

        result = default;
        return false;
    }

    // Tìm ô spawn an toàn: walkable, ≥ minNeighbors lối thoát trực tiếp,
    // và vùng liên thông ≥ minRegionSize ô — tránh player bị kẹt trong hốc không có đường ra.
    public bool TryFindWalkableWithSpace(Vector2Int preferredCell, int minRegionSize, int minNeighbors, out Vector2Int result)
    {
        int maxRadius = Mathf.Max(width, height);
        for (int radius = 0; radius <= maxRadius; radius++)
        {
            for (int y = preferredCell.y - radius; y <= preferredCell.y + radius; y++)
            {
                for (int x = preferredCell.x - radius; x <= preferredCell.x + radius; x++)
                {
                    Vector2Int candidate = new Vector2Int(x, y);
                    if (IsWalkable(candidate)
                        && CountWalkableNeighbors(candidate) >= minNeighbors
                        && CountConnectedWalkable(candidate, minRegionSize) >= minRegionSize)
                    {
                        result = candidate;
                        return true;
                    }
                }
            }
        }

        result = default;
        return false;
    }

    // Đếm số ô walkable liền kề 4 hướng (lối thoát trực tiếp).
    private int CountWalkableNeighbors(Vector2Int cell)
    {
        int count = 0;
        if (IsWalkable(cell + Vector2Int.up))    count++;
        if (IsWalkable(cell + Vector2Int.down))  count++;
        if (IsWalkable(cell + Vector2Int.left))  count++;
        if (IsWalkable(cell + Vector2Int.right)) count++;
        return count;
    }

    // BFS flood-fill đếm số ô walkable liên thông, dừng sớm khi đạt maxCount.
    private int CountConnectedWalkable(Vector2Int start, int maxCount)
    {
        var visited = new HashSet<Vector2Int>();
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start);
        visited.Add(start);

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        while (queue.Count > 0 && visited.Count < maxCount)
        {
            Vector2Int cur = queue.Dequeue();
            foreach (Vector2Int d in dirs)
            {
                Vector2Int nb = cur + d;
                if (!visited.Contains(nb) && IsWalkable(nb))
                {
                    visited.Add(nb);
                    queue.Enqueue(nb);
                }
            }
        }

        return visited.Count;
    }

    private void FitCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float mapWidthWorld = buildWidth * tileSize;
        float mapHeightWorld = buildHeight * tileSize;

        // zoom gần hơn một chút
        float padding = 0.85f;

        float verticalSize = mapHeightWorld / 2f;
        float horizontalSize = (mapWidthWorld / cam.aspect) / 2f;

        cam.orthographicSize =
            Mathf.Max(verticalSize, horizontalSize) * padding;

        cam.transform.position = new Vector3(0, 0, -10);
    }

    private char[][] ReadMapFile(string filePath, out int mapWidth, out int mapHeight)
    {
        string[] lines = File.ReadAllLines(filePath);
        if (lines.Length < 4)
        {
            throw new InvalidOperationException($"Invalid MAPF map file: {filePath}");
        }

        mapHeight = ParseHeaderInt(lines[1], "height");
        mapWidth = ParseHeaderInt(lines[2], "width");

        if (!lines[3].Trim().Equals("map", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("MAPF file line 4 must be 'map'.");
        }

        char[][] rows = new char[mapHeight][];
        for (int y = 0; y < mapHeight; y++)
        {
            string line = y + 4 < lines.Length ? lines[y + 4].TrimEnd() : string.Empty;
            rows[y] = new char[mapWidth];
            for (int x = 0; x < mapWidth; x++)
            {
                rows[y][x] = x < line.Length ? line[x] : '@';
            }
        }

        return rows;
    }

    private int ParseHeaderInt(string line, string key)
    {
        string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Invalid MAPF header line: {line}");
        }

        return int.Parse(parts[1]);
    }

    private void ComputeBuildWindow()
    {
        buildStartX = Mathf.Clamp(mapOffsetX, 0, Mathf.Max(0, width - 1));
        buildStartY = Mathf.Clamp(mapOffsetY, 0, Mathf.Max(0, height - 1));
        int availableWidth = width - buildStartX;
        int availableHeight = height - buildStartY;
        buildWidth = maxBuildWidth > 0 ? Mathf.Min(maxBuildWidth, availableWidth) : availableWidth;
        buildHeight = maxBuildHeight > 0 ? Mathf.Min(maxBuildHeight, availableHeight) : availableHeight;
    }

    private void BuildTiles()
    {
        CreateGroundBackground();
        for (int localY = 0; localY < buildHeight; localY++)
        {
            int mapY = buildStartY + localY;
            for (int localX = 0; localX < buildWidth; localX++)
            {
                int mapX = buildStartX + localX;
                char cell = grid[mapY][mapX];
                if (!IsCellWalkable(cell))
                    CreateTile(cell, new Vector2Int(mapX, mapY));
            }
        }
    }

    private void CreateGroundBackground()
    {
        Sprite sprite = groundSprite != null ? groundSprite : GetFallbackSprite();
        if (sprite == null) return;

        float mapW = buildWidth * tileSize;
        float mapH = buildHeight * tileSize;
        float sprW = Mathf.Max(sprite.bounds.size.x, 0.001f);
        float sprH = Mathf.Max(sprite.bounds.size.y, 0.001f);

        GameObject bg = new GameObject("Ground");
        bg.transform.SetParent(tilesParent, false);
        bg.transform.position = Vector3.zero;
        bg.transform.localScale = new Vector3(mapW / sprW, mapH / sprH, 1f);

        SpriteRenderer sr = bg.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.drawMode = SpriteDrawMode.Simple;
        sr.sortingOrder = -1;
    }

    private void CreateTile(char cell, Vector2Int mapCell)
    {
        GameObject tile = new GameObject("T");
        tile.transform.SetParent(tilesParent, false);
        tile.transform.position = CellToWorld(mapCell);
        tile.transform.localScale = Vector3.one * tileSize;

        SpriteRenderer spriteRenderer = tile.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = PickSprite(cell, mapCell);
        spriteRenderer.sortingOrder = IsCellWalkable(cell) ? 0 : 2;
        spriteRenderer.color = PickColor(cell);

        if (!IsCellWalkable(cell))
        {
            tile.layer = ResolveLayer(MovementObstacleLayerName, LegacyMovementObstacleLayerName);
            BoxCollider2D collider = tile.AddComponent<BoxCollider2D>();
            collider.size = Vector2.one;
            AddBulletHitbox(tile);
        }
    }

    private void AddBulletHitbox(GameObject tile)
    {
        GameObject hitbox = new GameObject("BulletHitbox");
        hitbox.layer = LayerMask.NameToLayer(BulletObstacleLayerName);
        hitbox.transform.SetParent(tile.transform, false);

        BoxCollider2D collider = hitbox.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        collider.size = Vector2.one;
    }

    private void CreateMapBounds()
    {
        float totalWidth = buildWidth * tileSize;
        float totalHeight = buildHeight * tileSize;
        float thickness = tileSize;
        CreateBoundary("MapBoundary_Top", new Vector2(0f, totalHeight / 2f + thickness / 2f), new Vector2(totalWidth + thickness * 2f, thickness));
        CreateBoundary("MapBoundary_Bottom", new Vector2(0f, -totalHeight / 2f - thickness / 2f), new Vector2(totalWidth + thickness * 2f, thickness));
        CreateBoundary("MapBoundary_Left", new Vector2(-totalWidth / 2f - thickness / 2f, 0f), new Vector2(thickness, totalHeight));
        CreateBoundary("MapBoundary_Right", new Vector2(totalWidth / 2f + thickness / 2f, 0f), new Vector2(thickness, totalHeight));
    }

    private void CreateBoundary(string name, Vector2 position, Vector2 size)
    {
        GameObject boundary = new GameObject(name);
        boundary.layer = ResolveLayer(MovementObstacleLayerName, LegacyMovementObstacleLayerName);
        boundary.transform.SetParent(tilesParent, false);
        boundary.transform.localPosition = position;

        BoxCollider2D collider = boundary.AddComponent<BoxCollider2D>();
        collider.size = size;
        AddBulletHitbox(boundary);
    }

    private int ResolveLayer(string layerName, string fallbackLayerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        return layer >= 0 ? layer : LayerMask.NameToLayer(fallbackLayerName);
    }

    private Sprite PickSprite(char cell, Vector2Int mapCell)
    {
        if (cell == '.' && alternateGroundSprite != null && (mapCell.x + mapCell.y) % 2 == 0)
        {
            return alternateGroundSprite;
        }

        if (cell == '.') return groundSprite != null ? groundSprite : GetFallbackSprite();
        if (cell == 'T') return treeSprite != null ? treeSprite : obstacleSprite != null ? obstacleSprite : GetFallbackSprite();
        if (cell == 'W') return waterSprite != null ? waterSprite : GetFallbackSprite();
        if (cell == 'S') return swampSprite != null ? swampSprite : groundSprite != null ? groundSprite : GetFallbackSprite();
        return obstacleSprite != null ? obstacleSprite : GetFallbackSprite();
    }

    private Color PickColor(char cell)
    {
        if (cell == 'W') return new Color(0.35f, 0.6f, 1f, 1f);
        if (cell == 'S') return new Color(0.65f, 0.55f, 0.8f, 1f);
        return Color.white;
    }

    private bool IsCellWalkable(char cell)
    {
        string cells = string.IsNullOrEmpty(walkableCells) ? "." : walkableCells;
        return cells.IndexOf(cell) >= 0;
    }

    private void ClearExistingTiles()
    {
        _destructibleCells.Clear();
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

    private Sprite GetFallbackSprite()
    {
        if (fallbackSprite != null)
        {
            return fallbackSprite;
        }

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply(false, true);
        fallbackSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
        return fallbackSprite;
    }

#if UNITY_EDITOR
    private Sprite LoadSpriteAsset(string assetPath)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (sprite != null)
        {
            return sprite;
        }

        Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(assetPath);
        if (tile != null && tile.sprite != null)
        {
            return tile.sprite;
        }

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is Sprite subSprite)
            {
                return subSprite;
            }
        }

        return null;
    }
#endif

    private void LoadDefaultSprites()
    {
#if UNITY_EDITOR
        groundSprite          = LoadSpriteAsset(groundSpritePath);
        alternateGroundSprite = LoadSpriteAsset(alternateGroundSpritePath);
        obstacleSprite        = LoadSpriteAsset(obstacleSpritePath);
        treeSprite            = LoadSpriteAsset(treeSpritePath);
#else
        groundSprite          = SpriteFromResources("MapTiles/tileGrass1");
        alternateGroundSprite = SpriteFromResources("MapTiles/tileGrass2");
        obstacleSprite        = SpriteFromResources("MapTiles/crateMetal");
        treeSprite            = SpriteFromResources("MapTiles/treeGreen_large");
#endif
    }

    private static Sprite SpriteFromResources(string path)
    {
        Sprite spr = Resources.Load<Sprite>(path);
        if (spr != null) return spr;
        Texture2D tex = Resources.Load<Texture2D>(path);
        if (tex == null) return null;
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
    }
}
