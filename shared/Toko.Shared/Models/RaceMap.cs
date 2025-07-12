using System.Drawing;
using System.Linq;

namespace Toko.Shared.Models
{
    public class RaceMap
    {
        public required List<TrackSegment> Segments { get; set; }
        public List<int> SegmentLengths => Segments.Select(s => s.LaneCells.FirstOrDefault()?.Count ?? 0).ToList();
        public int TotalCells => SegmentLengths.Sum();

        public class TrackSegment
        {
            public CellType DefaultType { get; set; }
            public SegmentDirection Direction { get; set; }
            public required List<List<Cell>> LaneCells { get; set; } // LaneCells[i] is all cells in lane i of this segment
            public int LaneCount => LaneCells.Count;
            public int CellCount => LaneCells.FirstOrDefault()?.Count ?? 0; // assuming all lanes have the same length
            public bool IsCorner => !(Direction is SegmentDirection.Left or SegmentDirection.Right or SegmentDirection.Up or SegmentDirection.Down);
            public bool IsIntermediate { get; set; } // Indicates if this segment is an intermediate segment created by combining two segments
        }
    }
    public enum SegmentDirection
    {
        Left,
        Right,
        Up,
        Down,
        UpRight,
        UpLeft,
        DownRight,
        DownLeft,
        RightUp,
        RightDown,
        LeftUp,
        LeftDown,
    }
    // road: normal, grass: slow, boost: fast, obstacle: stop, cornerLeft: turn left, cornerRight: turn right
    public enum CellType { Road, Grass, Boost, Obstacle, CornerLeft, CornerRight }
    public enum MapRenderingType {
        BothEdges,
        SingleEdge, 
        Plain,
        CornerEdge,
        RightSingleEdgeLowerLeftCornerEdge,
        RightSingleEdgeUpperLeftCornerEdge,
        CurveSmall,
        CurveSmallCornerEdge,
        CurveLargeSeg1,
        CurveLargeSeg2,
        CurveLargeSeg3
    }
    public enum MapRenderingRotation
    {
        Original = 0,
        Rotate90 = 90,
        Rotate180 = 180,
        Rotate270 = 270
    }
    public record Grid(MapRenderingType RenderingType, MapRenderingRotation Rotation);
    public record MapSnapshot(int TotalCells, List<MapSegmentSnapshot> Segments);
    public record MapSegmentSnapshot(string Type, int LaneCount, int CellCount, string Direction, bool IsIntermediate);
    public record Cell(Point Position, CellType Type, Grid? Grid)
    {
        public Cell WithPosition(Point newPosition)
        {
            return this with { Position = newPosition };
        }
        public Cell WithGrid(Grid? newGird)
        {
            return this with { Grid = newGird };
        }
    };
}
