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
            public required List<List<Point>> LaneCells { get; set; } // LaneCells[i] 就是第 i 车道上的所有格子
            public int LaneCount { get; set; }       // 直道=4，弯道=2
                                                     //public List<Point> Cells { get; set; }   // 每条车道的 (x,y) 网格坐标
        }
    }

}
