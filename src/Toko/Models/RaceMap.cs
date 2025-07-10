using System.Drawing;
using System.Linq;

namespace Toko.Models
{
    public class RaceMap
    {
        public required List<TrackSegment> Segments { get; set; }
        public List<int> SegmentLengths => Segments.Select(s => s.LaneCells.FirstOrDefault()?.Count ?? 0).ToList();
        public int TotalCells => SegmentLengths.Sum();

        public class TrackSegment
        {
            public TileType Type { get; set; }       
            public required List<List<Point>> LaneCells { get; set; } // LaneCells[i] is all cells in lane i of this segment
            public int LaneCount { get; set; }
        }
    }

}
