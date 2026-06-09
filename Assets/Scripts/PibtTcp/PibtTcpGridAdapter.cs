using System.Collections.Generic;
using UnityEngine;
using PibtTcp;

/// <summary>
/// Converts between Unity grid (Vector2Int) and the start-kit loc integer,
/// and builds the DTO objects used in PibtTcpProtocol.
/// </summary>
public static class PibtTcpGridAdapter
{
    // ─── Loc ↔ Cell ───────────────────────────────────────────────────────────

    /// <summary>Returns row-major location index from a grid cell.</summary>
    public static int CellToLoc(Vector2Int cell, int mapWidth)
    {
        return cell.y * mapWidth + cell.x;
    }

    /// <summary>Returns grid cell from a row-major location index.</summary>
    public static Vector2Int LocToCell(int loc, int mapWidth)
    {
        return new Vector2Int(loc % mapWidth, loc / mapWidth);
    }

    // ─── Orientation ──────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a Unity grid-cell facing delta to a start-kit orientation int.
    /// Cell coordinates are row-major: east = +x, south = +y.
    /// 0=east 1=south 2=west 3=north
    /// </summary>
    public static int DirectionToOrientation(Vector2Int facing)
    {
        if (facing == Vector2Int.right) return 0; // east
        if (facing == Vector2Int.up)    return 1; // south (+y in grid)
        if (facing == Vector2Int.left)  return 2; // west
        if (facing == Vector2Int.down)  return 3; // north (-y in grid)
        return 0; // default east
    }

    public static Vector2Int OrientationToCell(int orientation)
    {
        switch (orientation)
        {
            case 0: return Vector2Int.right; // east
            case 1: return Vector2Int.up;    // south (+y in grid)
            case 2: return Vector2Int.left;  // west
            case 3: return Vector2Int.down;  // north (-y in grid)
            default: return Vector2Int.right;
        }
    }

    public static int RotateClockwise(int orientation)
    {
        return (orientation + 1) % 4;
    }

    public static int RotateCounterClockwise(int orientation)
    {
        return (orientation + 3) % 4;
    }

    public static int ForwardDeltaLoc(int orientation, int mapWidth)
    {
        switch (orientation)
        {
            case 0: return 1;         // east
            case 1: return mapWidth;  // south
            case 2: return -1;        // west
            case 3: return -mapWidth; // north
            default: return 1;
        }
    }

    public static string BuildOrientationConventionReport(int mapWidth)
    {
        var rows = new List<string>(4);
        for (int orientation = 0; orientation < 4; orientation++)
        {
            Vector2Int facing = OrientationToCell(orientation);
            int roundTrip = DirectionToOrientation(facing);
            int cw = RotateClockwise(orientation);
            int ccw = RotateCounterClockwise(orientation);
            int fwDelta = ForwardDeltaLoc(orientation, mapWidth);
            rows.Add(
                $"ori={orientation} facing={facing} roundTrip={roundTrip} " +
                $"CR->{cw} CCR->{ccw} FWdelta={fwDelta}");
        }

        return string.Join(" | ", rows);
    }

    // ─── Map symbols ─────────────────────────────────────────────────────────

    /// <summary>
    /// Encodes a 2-D bool array (true = walkable) into a row-major symbol string.
    /// '.' = free, '@' = obstacle (matches start-kit convention).
    /// </summary>
    public static string EncodeTileMap(bool[,] walkable, int width, int height)
    {
        var sb = new System.Text.StringBuilder(width * height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                sb.Append(walkable[x, y] ? '.' : '@');
            }
        }
        return sb.ToString();
    }

    // ─── DTO builders ────────────────────────────────────────────────────────

    public static MapDto BuildMapDto(bool[,] walkable, int width, int height)
    {
        return new MapDto
        {
            width   = width,
            height  = height,
            symbols = EncodeTileMap(walkable, width, height)
        };
    }

    public static AgentStateDto BuildAgentState(
        int       agentId,
        Vector2Int cell,
        Vector2Int facing,
        Vector2Int goalCell,
        int        mapWidth)
    {
        return new AgentStateDto
        {
            id          = agentId,
            loc         = CellToLoc(cell, mapWidth),
            orientation = DirectionToOrientation(facing),
            goalLoc     = CellToLoc(goalCell, mapWidth)
        };
    }

    public static List<AgentStateDto> BuildAgentList(
        IReadOnlyList<(int id, Vector2Int cell, Vector2Int facing, Vector2Int goal)> agents,
        int mapWidth)
    {
        var list = new List<AgentStateDto>(agents.Count);
        foreach (var a in agents)
        {
            list.Add(BuildAgentState(a.id, a.cell, a.facing, a.goal, mapWidth));
        }
        return list;
    }
}
