using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One generated collider mesh and its statistics.
/// </summary>
public class GeneratedMeshInfo
{
    /// <summary>GameObject name for this piece, e.g. "Room_Treatment Lab" or "Passage_TL-CH".</summary>
    public string objectName;

    /// <summary>The generated mesh (not saved as an asset).</summary>
    public Mesh mesh;

    /// <summary>Index into BuildingLayout.rooms, or -1 for a passage.</summary>
    public int roomIndex = -1;

    /// <summary>Index into BuildingLayout.doorways for a passage, otherwise -1.</summary>
    public int doorwayIndex = -1;

    public int quadCount;
    public int triangleCount;

    /// <summary>Longest triangle edge in this mesh (ft), diagonals included.</summary>
    public float maxEdgeLength;
}

/// <summary>
/// Result of building geometry for a layout.
/// </summary>
public class TourGeometryBuildResult
{
    public readonly List<GeneratedMeshInfo> rooms = new List<GeneratedMeshInfo>();
    public readonly List<GeneratedMeshInfo> passages = new List<GeneratedMeshInfo>();

    public IEnumerable<GeneratedMeshInfo> All
    {
        get
        {
            foreach (GeneratedMeshInfo info in rooms) yield return info;
            foreach (GeneratedMeshInfo info in passages) yield return info;
        }
    }

    public int TotalTriangles
    {
        get
        {
            int total = 0;
            foreach (GeneratedMeshInfo info in All) total += info.triangleCount;
            return total;
        }
    }

    public float MaxEdgeLength
    {
        get
        {
            float max = 0f;
            foreach (GeneratedMeshInfo info in All) max = Mathf.Max(max, info.maxEdgeLength);
            return max;
        }
    }
}

/// <summary>
/// Builds inward-facing collider meshes from a validated BuildingLayout: per room a floor, a ceiling, and four
/// walls split around doorway openings (solid pieces plus a lintel above each opening); per gap-spanning doorway
/// a passage (threshold, head, two jambs). Positions are world-space feet; the generated root sits at the origin.
///
/// Winding: Unity treats a triangle (a, b, c) as front-facing along Cross(b - a, c - a). RaycastHit.normal on a
/// MeshCollider is that geometric face normal, so every quad is wound to face into its room or passage.
///
/// Pure: creates Mesh objects only; no scene, asset, or UnityEditor access. Usable at runtime.
/// </summary>
public static class TourGeometryBuilder
{
    #region Naming

    /// <summary>Prefix shared by every generated mesh name; used for leak checks.</summary>
    public static string MeshNamePrefix(BuildingLayout layout)
    {
        return $"TourGeometry_{layout.layoutName}_";
    }

    /// <summary>Name of the generated root GameObject.</summary>
    public static string RootName(BuildingLayout layout)
    {
        return $"TourGeometry_{layout.layoutName}";
    }

    #endregion

    #region Build

    /// <summary>
    /// Validates and builds. Returns null (and creates no meshes) if validation has errors;
    /// <paramref name="validation"/> is always set.
    /// </summary>
    public static TourGeometryBuildResult Build(BuildingLayout layout, out LayoutValidationResult validation)
    {
        validation = BuildingLayoutValidator.Validate(layout);
        if (validation.HasErrors)
            return null;

        TourGeometryBuildResult result = new TourGeometryBuildResult();
        string prefix = MeshNamePrefix(layout);

        // Openings per (room, side), gathered from the resolved doorway matches.
        Dictionary<long, List<Opening>> openings = CollectOpenings(validation);

        for (int i = 0; i < layout.rooms.Count; i++)
        {
            LayoutRoom room = layout.rooms[i];
            MeshAccumulator acc = new MeshAccumulator();

            BuildRoom(layout, room, i, openings, acc);

            string objectName = $"Room_{room.name}";
            result.rooms.Add(acc.ToInfo(prefix + objectName, objectName, i, -1));
        }

        foreach (ResolvedDoorway rd in validation.doorways)
        {
            if (!rd.needsPassage)
                continue;

            MeshAccumulator acc = new MeshAccumulator();
            BuildPassage(rd, acc);

            string objectName = $"Passage_{rd.doorway.name}";
            result.passages.Add(acc.ToInfo(prefix + objectName, objectName, -1, rd.doorwayIndex));
        }

        return result;
    }

    #endregion

    #region Rooms

    private struct Opening
    {
        public float alongMin;
        public float alongMax;
        public float topY;
    }

    private static long WallKey(int roomIndex, WallSide side)
    {
        return ((long)roomIndex << 8) | (long)side;
    }

    private static Dictionary<long, List<Opening>> CollectOpenings(LayoutValidationResult validation)
    {
        Dictionary<long, List<Opening>> openings = new Dictionary<long, List<Opening>>();

        foreach (ResolvedDoorway rd in validation.doorways)
        {
            LayoutDoorway d = rd.doorway;
            foreach (DoorwayWallMatch m in rd.matches)
            {
                long key = WallKey(m.roomIndex, m.wall.side);
                if (!openings.TryGetValue(key, out List<Opening> list))
                {
                    list = new List<Opening>();
                    openings[key] = list;
                }

                // Validation guarantees all matched rooms share rd.floorY.
                list.Add(new Opening { alongMin = d.AlongMin, alongMax = d.AlongMax, topY = rd.floorY + d.height });
            }
        }

        foreach (List<Opening> list in openings.Values)
            list.Sort((a, b) => a.alongMin.CompareTo(b.alongMin));

        return openings;
    }

    private static void BuildRoom(BuildingLayout layout, LayoutRoom room, int roomIndex,
                                  Dictionary<long, List<Opening>> openings, MeshAccumulator acc)
    {
        float floorY = layout.GetFloorY(room);
        float ceilingY = layout.GetCeilingY(room);

        // Floor (faces up) and ceiling (faces down).
        acc.AddQuad(new Vector3(room.xMin, floorY, room.zMin),
                    new Vector3(room.xMax, floorY, room.zMin),
                    new Vector3(room.xMin, floorY, room.zMax),
                    Vector3.up);

        acc.AddQuad(new Vector3(room.xMin, ceilingY, room.zMin),
                    new Vector3(room.xMax, ceilingY, room.zMin),
                    new Vector3(room.xMin, ceilingY, room.zMax),
                    Vector3.down);

        // Walls, split around openings.
        foreach (WallSide side in LayoutMath.AllSides)
        {
            RoomWall wall = room.GetWall(side);
            openings.TryGetValue(WallKey(roomIndex, side), out List<Opening> wallOpenings);

            float cursor = wall.min;

            if (wallOpenings != null)
            {
                foreach (Opening o in wallOpenings)
                {
                    // Solid piece before the opening.
                    if (o.alongMin - cursor > LayoutConstants.MinPieceLength)
                        AddWallPiece(acc, wall, cursor, o.alongMin, floorY, ceilingY);

                    // Lintel above the opening.
                    if (ceilingY - o.topY > LayoutConstants.MinPieceLength && o.alongMax - o.alongMin > LayoutConstants.MinPieceLength)
                        AddWallPiece(acc, wall, o.alongMin, o.alongMax, o.topY, ceilingY);

                    cursor = Mathf.Max(cursor, o.alongMax);
                }
            }

            // Solid piece after the last opening (or the whole wall).
            if (wall.max - cursor > LayoutConstants.MinPieceLength)
                AddWallPiece(acc, wall, cursor, wall.max, floorY, ceilingY);
        }
    }

    private static void AddWallPiece(MeshAccumulator acc, RoomWall wall, float along0, float along1, float y0, float y1)
    {
        acc.AddQuad(LayoutMath.WallToWorld(wall.runsAlong, wall.line, along0, y0),
                    LayoutMath.WallToWorld(wall.runsAlong, wall.line, along1, y0),
                    LayoutMath.WallToWorld(wall.runsAlong, wall.line, along0, y1),
                    wall.inwardNormal);
    }

    #endregion

    #region Passages

    /// <summary>
    /// Closes the wall-thickness gap under a doorway: threshold floor, head, and two jambs, all facing into the
    /// passage. The two ends (toward each room) stay open.
    /// </summary>
    private static void BuildPassage(ResolvedDoorway rd, MeshAccumulator acc)
    {
        LayoutDoorway d = rd.doorway;
        WallAxis axis = d.runsAlong;

        float l0 = rd.passageLineMin;
        float l1 = rd.passageLineMax;
        float a0 = d.AlongMin;
        float a1 = d.AlongMax;
        float y0 = rd.floorY;
        float y1 = rd.floorY + d.height;

        Vector3 alongDir = axis == WallAxis.Z ? Vector3.forward : Vector3.right;

        // Threshold (faces up).
        acc.AddQuad(LayoutMath.WallToWorld(axis, l0, a0, y0),
                    LayoutMath.WallToWorld(axis, l1, a0, y0),
                    LayoutMath.WallToWorld(axis, l0, a1, y0),
                    Vector3.up);

        // Head (faces down).
        acc.AddQuad(LayoutMath.WallToWorld(axis, l0, a0, y1),
                    LayoutMath.WallToWorld(axis, l1, a0, y1),
                    LayoutMath.WallToWorld(axis, l0, a1, y1),
                    Vector3.down);

        // Jamb at alongMin (faces +along) and at alongMax (faces -along).
        acc.AddQuad(LayoutMath.WallToWorld(axis, l0, a0, y0),
                    LayoutMath.WallToWorld(axis, l1, a0, y0),
                    LayoutMath.WallToWorld(axis, l0, a0, y1),
                    alongDir);

        acc.AddQuad(LayoutMath.WallToWorld(axis, l0, a1, y0),
                    LayoutMath.WallToWorld(axis, l1, a1, y0),
                    LayoutMath.WallToWorld(axis, l0, a1, y1),
                    -alongDir);
    }

    #endregion

    #region Mesh Accumulation

    /// <summary>
    /// Collects subdivided, correctly wound quads into vertex/normal/index lists.
    /// </summary>
    private class MeshAccumulator
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<int> _triangles = new List<int>();
        private int _quadCount;
        private float _maxEdge;

        /// <summary>
        /// Adds the rectangle with corner <paramref name="origin"/> and edges (uEnd - origin) and (vEnd - origin),
        /// subdivided so no cell edge exceeds LayoutConstants.MaxQuadEdge, wound to face <paramref name="facing"/>.
        /// </summary>
        public void AddQuad(Vector3 origin, Vector3 uEnd, Vector3 vEnd, Vector3 facing)
        {
            Vector3 u = uEnd - origin;
            Vector3 v = vEnd - origin;

            int nu = Mathf.Max(1, Mathf.CeilToInt(u.magnitude / LayoutConstants.MaxQuadEdge - 1e-4f));
            int nv = Mathf.Max(1, Mathf.CeilToInt(v.magnitude / LayoutConstants.MaxQuadEdge - 1e-4f));

            Vector3 du = u / nu;
            Vector3 dv = v / nv;

            // Triangles (a, b, c) and (a, c, d) on cell corners a=(i,j), b=(i+1,j), c=(i+1,j+1), d=(i,j+1)
            // face along Cross(du, dv). Flip if that disagrees with the requested facing.
            bool flip = Vector3.Dot(Vector3.Cross(du, dv), facing) < 0f;
            Vector3 normal = facing.normalized;

            int baseIndex = _vertices.Count;
            for (int j = 0; j <= nv; j++)
            {
                for (int i = 0; i <= nu; i++)
                {
                    _vertices.Add(origin + du * i + dv * j);
                    _normals.Add(normal);
                }
            }

            int stride = nu + 1;
            for (int j = 0; j < nv; j++)
            {
                for (int i = 0; i < nu; i++)
                {
                    int a = baseIndex + j * stride + i;
                    int b = a + 1;
                    int c = a + stride + 1;
                    int d = a + stride;

                    if (flip)
                    {
                        _triangles.Add(a); _triangles.Add(c); _triangles.Add(b);
                        _triangles.Add(a); _triangles.Add(d); _triangles.Add(c);
                    }
                    else
                    {
                        _triangles.Add(a); _triangles.Add(b); _triangles.Add(c);
                        _triangles.Add(a); _triangles.Add(c); _triangles.Add(d);
                    }
                }
            }

            _quadCount++;
            _maxEdge = Mathf.Max(_maxEdge, du.magnitude, dv.magnitude, (du + dv).magnitude);
        }

        public GeneratedMeshInfo ToInfo(string meshName, string objectName, int roomIndex, int doorwayIndex)
        {
            Mesh mesh = new Mesh { name = meshName };
            mesh.indexFormat = _vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();

            return new GeneratedMeshInfo
            {
                objectName = objectName,
                mesh = mesh,
                roomIndex = roomIndex,
                doorwayIndex = doorwayIndex,
                quadCount = _quadCount,
                triangleCount = _triangles.Count / 3,
                maxEdgeLength = _maxEdge
            };
        }
    }

    #endregion
}
