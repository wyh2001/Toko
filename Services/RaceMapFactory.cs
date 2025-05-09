using System.Collections.Generic;
using System.Drawing;
using Toko.Models;

namespace Toko.Services
{
    public static class RaceMapFactory
    {
        public static RaceMap CreateDefaultMap()
        {
            var segments = new List<TrackSegment>();

            // 1) 第一段：水平直道，4 道车道，长度 5
            //    Cells 按 (x, laneIndex) 展开
            var straight1 = new TrackSegment
            {
                Type = TileType.Road,
                LaneCount = 4,
                Cells = new List<Point>()
            };
            for (int x = 0; x < 5; x++)
                for (int lane = 0; lane < 4; lane++)
                    straight1.Cells.Add(new Point(x, lane));
            segments.Add(straight1);

            // 2) 第二段：向下的弯道，2 道车道，长度 5
            //    Cells 按 (x:4, y:0..4) 两道车道 (laneIndex 0/1)
            var curve1 = new TrackSegment
            {
                Type = TileType.CornerRight,
                LaneCount = 2,
                Cells = new List<Point>()
            };
            for (int y = 0; y < 5; y++)
                for (int lane = 0; lane < 2; lane++)
                    curve1.Cells.Add(new Point(4 + lane, y));
            segments.Add(curve1);

            // 3) 第三段：水平直道，4 道车道，长度 5，向左
            var straight2 = new TrackSegment
            {
                Type = TileType.Road,
                LaneCount = 4,
                Cells = new List<Point>()
            };
            for (int x = 4; x >= 0; x--)
                for (int lane = 0; lane < 4; lane++)
                    straight2.Cells.Add(new Point(x, 5 + lane));
            segments.Add(straight2);

            // 4) 第四段：向上弯道，2 道车道，长度 5
            var curve2 = new TrackSegment
            {
                Type = TileType.CornerLeft,
                LaneCount = 2,
                Cells = new List<Point>()
            };
            for (int y = 4; y >= 0; y--)
                for (int lane = 0; lane < 2; lane++)
                    curve2.Cells.Add(new Point(lane, 9 - y));
            segments.Add(curve2);

            // 构造并返回
            return new RaceMap { Segments = segments };
        }
    }
}
