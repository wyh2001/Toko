using System.Drawing;
using Toko.Shared.Models;
using static Toko.Shared.Models.RaceMap;


namespace Toko.Shared.Services
{
    public static class RaceMapFactory
    {
        public static RaceMap CreateDefaultMap()
        {
            var segments = new List<TrackSegment>
            {
                CreateNormalSegment(TileType.Road, 2, 3, SegmentDirection.Right),
                CreateNormalSegment(TileType.Road, 1, 7, SegmentDirection.Down),
                CreateNormalSegment(TileType.Road, 2, 3, SegmentDirection.Left),
                CreateNormalSegment(TileType.Road, 1, 7, SegmentDirection.Up)
            };
            return GenerateFinalMapWithIntermediate(segments);
        }

        public static RaceMap CreateMap(List<MapSegmentSnapshot> SegmentSnapshots)
        {
            var segments = SegmentSnapshots.Where(s => !s.IsIntermediate).Select(snapshot =>
                CreateNormalSegment(
                    Enum.Parse<TileType>(snapshot.Type),
                    snapshot.LaneCount,
                    snapshot.CellCount,
                    Enum.Parse<SegmentDirection>(snapshot.Direction),
                    snapshot.IsIntermediate)).ToList();
            return GenerateFinalMapWithIntermediate(segments);
        }

        // note: not all maps connect at both ends, should treat them seperately at frontend
        // a "teleportation" point should be shown to the user when the map is not circular
        public static RaceMap GenerateFinalMapWithIntermediate(List<TrackSegment> segments)
        {
            if (segments is null || segments.Count is 0)
                throw new ArgumentException("Segments cannot be null or empty");
            if (segments.Count is 1)
            {
                return new RaceMap { Segments = segments }; // circle track
            }
            else
            {
                var result = new List<TrackSegment>(segments.Count * 3); // extra space
                for (int i = 0; i < segments.Count; i++)
                {
                    var current = segments[i];
                    var next = segments[(i + 1) % segments.Count];

                    result.Add(current);
                    //result.Add(CreateIntermediateSegment(current, next));
                    var intermediate = CreateIntermediateSegment(current, next);
                    if (intermediate is not null)
                        result.AddRange(intermediate);
                }
                return new RaceMap { Segments = result };
            }
        }

        private static List<TrackSegment> CreateIntermediateSegment(TrackSegment current, TrackSegment next)
        {
            var intermediate = new List<TrackSegment>();
            if (!IsBasic(current.Direction) || !IsBasic(next.Direction))
                throw new ArgumentException(
                    "Only basic directions (Left, Right, Up, Down) are allowed for current/next");
            if (current.Direction == next.Direction && current.LaneCount == next.LaneCount)
                throw new ArgumentException(
                    "Current and next segments shouldn't have the same direction and lanecount");
            // note: in this case, the racer may not move forward at specific tile and must change lane
            // this could be implemented later by using a special segment type
            if (current.Direction == next.Direction)
                throw new NotImplementedException(
                    "Combining segments with the same direction is not implemented yet");
            if (current.LaneCount < 1 || next.LaneCount < 1)
                throw new ArgumentException("Lane count must be at least 1 for both segments");
            var direction = Combine(current.Direction, next.Direction);
            if (current.LaneCount * next.LaneCount <= 2)
            {
                intermediate =
                [
                    CreateNormalSegment(current.DefaultType, current.LaneCount, next.LaneCount, direction, true)
                ];
            }
            else
            {
                intermediate =
                [
                    CreateNormalSegment(current.DefaultType, current.LaneCount, next.LaneCount - 1, direction, true),
                    CreateNormalSegment(current.DefaultType, current.LaneCount - 1, 1, direction, true)
                ];
            }
            return intermediate;
        }

        private static readonly HashSet<SegmentDirection> _basicDirections =
    [
        SegmentDirection.Left,
        SegmentDirection.Right,
        SegmentDirection.Up,
        SegmentDirection.Down
    ];

        private static bool IsBasic(SegmentDirection dir) => _basicDirections.Contains(dir);

        private static SegmentDirection Combine(SegmentDirection from, SegmentDirection to)
        {
            if (from == to)
                return from; // No change, just return the same direction
            return (from, to) switch
            {
                (SegmentDirection.Left, SegmentDirection.Up) => SegmentDirection.LeftUp,
                (SegmentDirection.Left, SegmentDirection.Down) => SegmentDirection.LeftDown,
                (SegmentDirection.Right, SegmentDirection.Up) => SegmentDirection.RightUp,
                (SegmentDirection.Right, SegmentDirection.Down) => SegmentDirection.RightDown,

                (SegmentDirection.Up, SegmentDirection.Left) => SegmentDirection.UpLeft,
                (SegmentDirection.Up, SegmentDirection.Right) => SegmentDirection.UpRight,
                (SegmentDirection.Down, SegmentDirection.Left) => SegmentDirection.DownLeft,
                (SegmentDirection.Down, SegmentDirection.Right) => SegmentDirection.DownRight,

                _ => throw new ArgumentException(
                         $"Directions “{from} → {to}” can not be combined")
            };
        }

        public static TrackSegment CreateNormalSegment(TileType type, int laneCount, int cellCount, SegmentDirection direction)
        {
            //var segments = new List<TrackSegment>();
            var seg = new TrackSegment
            {
                DefaultType = type,
                //LaneCount = laneCount,
                LaneCells = new List<List<Cell>>(laneCount),
                Direction = direction,
                IsIntermediate = false
            };

            for (int lane = 0; lane < laneCount; lane++)
                seg.LaneCells.Add(new List<Cell>(cellCount));

            for (int x = 0; x < cellCount; x++)
                for (int lane = 0; lane < laneCount; lane++)
                    seg.LaneCells[lane].Add(new Cell(new Point(x, lane), type));

            return seg;
        }

        public static TrackSegment CreateNormalSegment(TileType type, int laneCount, int cellCount, SegmentDirection direction, bool IsIntermediate)
        {
            //var segments = new List<TrackSegment>();
            var seg = new TrackSegment
            {
                DefaultType = type,
                //LaneCount = laneCount,
                LaneCells = new List<List<Cell>>(laneCount),
                Direction = direction,
                IsIntermediate = IsIntermediate
            };

            for (int lane = 0; lane < laneCount; lane++)
                seg.LaneCells.Add(new List<Cell>(cellCount));

            for (int x = 0; x < cellCount; x++)
                for (int lane = 0; lane < laneCount; lane++)
                    seg.LaneCells[lane].Add(new Cell(new Point(x, lane), type));

            return seg;
        }
    }
}
