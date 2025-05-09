using System.Drawing;

namespace Toko.Models
{
    public class TrackSegment
    {
        public TileType Type { get; set; }       // Road 或 Corner
        public int LaneCount { get; set; }       // 直道=4，弯道=2
        public List<Point> Cells { get; set; }   // 每条车道的 (x,y) 网格坐标
    }

}
