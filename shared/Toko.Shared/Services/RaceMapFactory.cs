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
                CreateNormalSegment(CellType.Road, 2, 3, SegmentDirection.Right),
                CreateNormalSegment(CellType.Road, 2, 7, SegmentDirection.Down),
                CreateNormalSegment(CellType.Road, 5, 3, SegmentDirection.Left),
                CreateNormalSegment(CellType.Road, 1, 7, SegmentDirection.Up)
            };
            return GenerateFinalMapWithIntermediate(segments);
        }

        public static RaceMap CreateMap(List<MapSegmentSnapshot> segmentSnapshots)
        {
            var segments = segmentSnapshots.Where(s => !s.IsIntermediate).Select(snapshot =>
                CreateNormalSegment(
                    Enum.Parse<CellType>(snapshot.Type),
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
                ReAssignPositionToCells(segments, new Point(0, 0));
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
                ReAssignPositionToCells(result, new Point(0, 0));
                return new RaceMap { Segments = result };
            }
        }

        public static RaceMap GenerateFinalMapWithIntermediate(List<TrackSegment> segments, Point startPoint)
        {
            if (segments is null || segments.Count is 0)
                throw new ArgumentException("Segments cannot be null or empty");
            if (segments.Count is 1)
            {
                ReAssignPositionToCells(segments, startPoint);
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
                ReAssignPositionToCells(result, startPoint);
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
                    //CreateNormalSegment(current.DefaultType, current.LaneCount, next.LaneCount - 1, direction, true),
                    //CreateNormalSegment(current.DefaultType, current.LaneCount - 1, 1, direction, true)
                    CreateNormalSegment(current.DefaultType, current.LaneCount, next.LaneCount, direction, true)
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

        public static TrackSegment CreateNormalSegment(CellType type, int laneCount, int cellCount, SegmentDirection direction)
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
                    seg.LaneCells[lane].Add(new Cell(new Point(x, lane), type, null));

            return seg;
        }

        public static TrackSegment CreateNormalSegment(CellType type, int laneCount, int cellCount, SegmentDirection direction, bool IsIntermediate)
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
                    seg.LaneCells[lane].Add(new Cell(new Point(x, lane), type, null));

            return seg;
        }

        private static readonly IReadOnlyDictionary<SegmentDirection, int> DirFactor = new Dictionary<SegmentDirection, int>
        {
            { SegmentDirection.Left, 1 },
            { SegmentDirection.Right, 1 },
            { SegmentDirection.Up, 0 },
            { SegmentDirection.Down, 0 },
            { SegmentDirection.LeftUp, 1 },
            { SegmentDirection.LeftDown, 1 },
            { SegmentDirection.RightUp, 0 },
            { SegmentDirection.RightDown, 3 },
            { SegmentDirection.UpLeft, 1 },
            { SegmentDirection.UpRight, 0 },
            { SegmentDirection.DownLeft, 2 },
            { SegmentDirection.DownRight, 0 }
        };

        private static MapRenderingRotation ToRotation(int deg)
        {
            deg = ((deg % 360) + 360) % 360;
            return deg switch
            {
                0 => MapRenderingRotation.Original,
                90 => MapRenderingRotation.Rotate90,
                180 => MapRenderingRotation.Rotate180,
                270 => MapRenderingRotation.Rotate270,
                _ => throw new ArgumentOutOfRangeException(nameof(deg),
                         $"Rotation must be a multiple of 90°, got {deg}")
            };
        }

        private static (MapRenderingType type, MapRenderingRotation rot) ResolveBasicGrid(SegmentDirection dir, int lane, int laneCount)
        {
            bool isBasic = IsBasic(dir);
            bool isSingleLane = laneCount == 1;
            bool isSideLane = lane == 0 || lane == laneCount - 1;
            //bool isNextFirstLaneCurve = IsFirstLaneCurve(nextDir);
            var f = DirFactor[dir];
            if (isBasic)
            {
                if (isSingleLane)
                {
                    //if (laneCount > 2 && nextCellCount == 1 && ((isNextFirstLaneCurve&&laneIndex==0)||(!isNextFirstLaneCurve&&laneCount==)))
                    //{

                    //}
                    int deg = f * 90;
                    return (MapRenderingType.BothEdges, ToRotation(deg));
                }

                if (isSideLane)
                {
                    int deg = f * 270;
                    if (lane == laneCount - 1) deg += 180;
                    return (MapRenderingType.SingleEdge, ToRotation(deg));
                }

                return (MapRenderingType.Plain, MapRenderingRotation.Original);
            }
            else
            {
                throw new ArgumentException($"Direction {dir} is not a basic direction");
            }
        }

        private static (MapRenderingType type, MapRenderingRotation rot)
    ResolveGrid(SegmentDirection dir, int laneIndex, int laneCount, int nextLaneCount, int cellIndex, int cellLength, TrackSegment lastSeg, TrackSegment seg)
        {
            bool isBasic = IsBasic(dir);
            bool isSingleLane = laneCount == 1;
            bool isSideLane = laneIndex == 0 || laneIndex == laneCount - 1;
            var f = DirFactor[dir];
            if (isBasic)
            {
                if (lastSeg.LaneCount == 1 && (laneCount * lastSeg.LaneCount) > 2)
                {
                    //var cell = GetSpecialCurveCell(seg, lastSeg.Direction).WithGrid(new Grid(MapRenderingType.CurveLargeSeg3, ToRotation(f*90)));
                    if ((!IsFirstLaneCurve(lastSeg.Direction) && laneIndex == laneCount - 1 && cellIndex == 0) ||
                        (IsFirstLaneCurve(lastSeg.Direction) && laneIndex == 0 && cellIndex == 0))
                    {
                        return (MapRenderingType.CurveLargeSeg3, ToRotation(DirFactor[lastSeg.Direction] * 270));
                    }

                }
                return ResolveBasicGrid(dir, laneIndex, laneCount);
            }
            if (isSingleLane && cellLength == 1)
            {
                int deg = f * 90;
                return (MapRenderingType.CurveSmallCornerEdge, ToRotation(deg));
            }
            var product = laneCount * nextLaneCount;
            var isFirstLaneCurve = IsFirstLaneCurve(dir);
            if (product == 2)
            {
                if (laneCount == 2)
                {
                    if ((laneIndex is 0 && isFirstLaneCurve) || (laneIndex is 1 && !isFirstLaneCurve))
                    {
                        int deg = f * 270;
                        return (MapRenderingType.CurveSmall, ToRotation(deg));
                    }
                    else
                    {
                        int deg = f * 270 + 270;
                        return (MapRenderingType.RightSingleEdgeLowerLeftCornerEdge, ToRotation(deg));
                    }
                }
                else
                {
                    // The first cell of the transition (index 0) is the inner corner tile.
                    bool isInnerCornerTile = (cellIndex == 0);

                    int deg = (f * 270) % 360;

                    // The inner corner tile ALWAYS needs a 180-degree rotation correction.
                    if (isInnerCornerTile)
                    {
                        deg = (deg + 180) % 360;
                    }

                    // Return the correct type and the final, correct rotation.
                    if (isInnerCornerTile)
                    {
                        return (MapRenderingType.RightSingleEdgeUpperLeftCornerEdge, ToRotation(deg));
                    }
                    else
                    {
                        return (MapRenderingType.CurveSmall, ToRotation(deg));
                    }
                }
            }
            else // product > 2
            {
                if (cellLength is 1)
                {
                    var lastF = DirFactor[dir];
                    int lastDeg = lastF * 270;
                    var lastBoundaryCell = GetSpecialCurveCell(lastSeg, dir).WithGrid(new Grid(MapRenderingType.CurveLargeSeg1, ToRotation(lastDeg)));
                    var laneIndexToUpdate = IsFirstLaneCurve(dir) ? 0 : lastSeg.LaneCount - 1;
                    lastSeg.LaneCells[laneIndexToUpdate][lastSeg.CellCount - 1] = lastBoundaryCell;
                    if ((laneIndex is 0 && isFirstLaneCurve) || (laneIndex == (laneCount - 1) && !isFirstLaneCurve))
                    {
                        // since there is only one cell per line
                        int deg = f * 270;
                        return (MapRenderingType.CurveLargeSeg2, ToRotation(deg));
                    }
                    else if ((laneIndex is 1 && isFirstLaneCurve) || (laneIndex == (laneCount - 2) && !isFirstLaneCurve))
                    {
                        int deg = f * 270;
                        return (MapRenderingType.CurveLargeSeg3, ToRotation(deg));
                    }
                    else
                    {
                        int deg = 0;
                        if (laneIndex == 0 || (!isFirstLaneCurve && laneIndex == laneCount - 1))
                        {
                            deg += 180;
                        }
                        return (MapRenderingType.SingleEdge, ToRotation(deg));
                    }
                }
                if ((laneIndex is 0 && isFirstLaneCurve) || (laneIndex == (laneCount - 1) && !isFirstLaneCurve))
                {
                    if (cellIndex == cellLength - 2) // seg1
                    {
                        int deg = f * 270;
                        return (MapRenderingType.CurveLargeSeg1, ToRotation(deg));

                    }
                    else if (cellIndex == cellLength - 1) // seg2
                    {
                        int deg = f * 270;
                        return (MapRenderingType.CurveLargeSeg2, ToRotation(deg));
                    }
                    else
                    {
                        int deg = f * 90;
                        return (MapRenderingType.SingleEdge, ToRotation(deg));
                    }

                }
                else if ((laneIndex is 1 && isFirstLaneCurve) || (laneIndex == (laneCount - 2) && !isFirstLaneCurve))
                {
                    if (cellIndex == cellLength - 1) // seg3
                    {
                        int deg = f * 270;
                        return (MapRenderingType.CurveLargeSeg3, ToRotation(deg));
                    }
                    else if (cellIndex == 0)
                    {
                        int deg = f * 270;
                        return (MapRenderingType.CornerEdge, ToRotation(deg));
                    }
                    else
                    {
                        int deg = f * 90;
                        return (MapRenderingType.Plain, ToRotation(deg));
                    }
                }
                else if (isSideLane)
                {
                    if (cellIndex == 0)
                    {
                        int deg = f * 270;
                        return (MapRenderingType.CornerEdge, ToRotation(deg));
                    }
                    else if (cellIndex == cellLength - 1)
                    {
                        int deg = f * 90;
                        return (MapRenderingType.SingleEdge, ToRotation(deg + 180));
                    }
                    else
                    {
                        int deg = f * 90;
                        return (MapRenderingType.Plain, ToRotation(deg));
                    }
                }
                else
                {
                    if (cellIndex == cellLength - 1)
                    {
                        int deg = f * 90;
                        return (MapRenderingType.SingleEdge, ToRotation(deg + 180));
                    }
                    else
                    {
                        int deg = f * 90;
                        return (MapRenderingType.Plain, ToRotation(deg));
                    }
                }
            }
        }

        public static void ReAssignPositionToCells(List<TrackSegment> segs, Point startPoint)
        {
            Point pre = startPoint;
            Grid currentGrid;
            for (int i = 0; i < segs.Count; i++)
            {
                var segment = segs[i];
                var direction = segment.Direction;
                //int rotationFactor = DirectionToRotationFactor[direction];
                bool isBasic = IsBasic(direction);
                //bool isSingleLane = segment.LaneCount == 1;
                //bool isDoubleLane = segment.LaneCount == 2;
                for (int lane = 0; lane < segment.LaneCount; lane++)
                {
                    for (int cellIndex = 0; cellIndex < segment.CellCount; cellIndex++)
                    {
                        var cell = segment.LaneCells[lane][cellIndex];
                        if (!isBasic)
                        {
                            if (i - 1 < 0)
                            {
                                throw new InvalidOperationException("It is not expected to have no previous segment for a non-basic one");
                            }
                            (var gridType, var rotation) = ResolveGrid(direction, lane, segment.LaneCount, segs[(i + 1) % segs.Count].LaneCount, cellIndex, segment.CellCount, segs[i - 1], segment);
                            currentGrid = new Grid(gridType, rotation);
                        }
                        else
                        {
                            (var gridType, var rotation) = ResolveGrid(direction, lane, segment.LaneCount, segs[(i + 1) % segs.Count].LaneCount, cellIndex, segment.CellCount, segs[((i - 1) + segs.Count) % segs.Count], segment);
                            currentGrid = new Grid(gridType, rotation);
                        }
                        // Reassign position based on direction
                        switch (direction)
                        {
                            case SegmentDirection.Left:
                            case SegmentDirection.LeftUp:
                            case SegmentDirection.LeftDown:
                                segment.LaneCells[lane][cellIndex] =
                                    cell.WithPosition(new Point(pre.X - cellIndex, pre.Y + lane))
                                    .WithGrid(currentGrid);
                                break;
                            case SegmentDirection.Right:
                            case SegmentDirection.RightUp:
                            case SegmentDirection.RightDown:
                                segment.LaneCells[lane][cellIndex] = cell.WithPosition(new Point(pre.X + cellIndex, pre.Y + lane))
                                .WithGrid(currentGrid);
                                break;
                            case SegmentDirection.Up:
                            case SegmentDirection.UpRight:
                            case SegmentDirection.UpLeft:
                                segment.LaneCells[lane][cellIndex] = cell.WithPosition(new Point(pre.X + lane, pre.Y + cellIndex))
                                .WithGrid(currentGrid);
                                break;
                            case SegmentDirection.Down:
                            case SegmentDirection.DownRight:
                            case SegmentDirection.DownLeft:
                                segment.LaneCells[lane][cellIndex] = cell.WithPosition(new Point(pre.X + lane, pre.Y - cellIndex))
                                .WithGrid(currentGrid);
                                break;
                            default:
                                throw new ArgumentException($"Unsupported direction: {direction}");
                        }
                    }
                }
                // Update pre to the last cell of the first lane of current segment
                if (i != segs.Count - 1) // not the last segment
                {
                    //var lastCell = segment.LaneCells[0][segment.CellCount - 1];
                    var nextDirection = segs[i + 1].Direction;
                    Cell lastCell = GetBoundaryCell(segment, nextDirection);
                    pre = GetNextStartPoint(nextDirection, lastCell);
                }
            }
        }

        private static Cell GetBoundaryCell(RaceMap.TrackSegment segment, SegmentDirection nextDir)
        {
            var cells = segment.LaneCells.SelectMany(l => l);

            return nextDir switch
            {
                SegmentDirection.Right or SegmentDirection.RightUp or SegmentDirection.RightDown
                    // Need largest X and, when equal, smallest Y
                    => cells.MaxBy(c => (c.Position.X, -c.Position.Y))!,

                SegmentDirection.Left or SegmentDirection.LeftUp or SegmentDirection.LeftDown
                    // Need smallest X and Y -> negate both to turn “smallest” into “largest”
                    => cells.MaxBy(c => (-c.Position.X, -c.Position.Y))!,

                SegmentDirection.Up or SegmentDirection.UpRight or SegmentDirection.UpLeft
                    // Need smallest X, largest Y -> negate X only
                    => cells.MaxBy(c => (-c.Position.X, c.Position.Y))!,

                SegmentDirection.Down or SegmentDirection.DownRight or SegmentDirection.DownLeft
                    // Same rule as “Left” (min X, min Y)
                    => cells.MaxBy(c => (-c.Position.X, -c.Position.Y))!,

                _ => throw new ArgumentException($"Unsupported direction: {nextDir}")
            };
        }

        private static Cell GetSpecialCurveCell(RaceMap.TrackSegment segment, SegmentDirection nextDir)
        {
            var cells = segment.LaneCells.SelectMany(l => l);

            return nextDir switch
            {
                SegmentDirection.RightDown or SegmentDirection.UpLeft
                    // largest X and Y
                    => cells.MaxBy(c => (c.Position.X, c.Position.Y))!,
                SegmentDirection.RightUp or SegmentDirection.DownLeft
                    // Need largest X and, when equal, smallest Y
                    => cells.MaxBy(c => (c.Position.X, -c.Position.Y))!,

                SegmentDirection.LeftUp
                    // Need smallest X and Y -> negate both to turn “smallest” into “largest”
                    => cells.MaxBy(c => (-c.Position.X, -c.Position.Y))!,

                SegmentDirection.UpRight or SegmentDirection.LeftDown
                    // Need smallest X, largest Y -> negate X only
                    => cells.MaxBy(c => (-c.Position.X, c.Position.Y))!,

                SegmentDirection.DownRight
                    // Same rule as “Left” (min X, min Y)
                    => cells.MaxBy(c => (-c.Position.X, -c.Position.Y))!,

                _ => throw new ArgumentException($"Unsupported direction: {nextDir}")
            };
        }

        private static Cell GetSpecialStartingCurveCell(RaceMap.TrackSegment segment, SegmentDirection nextDir)
        {
            var cells = segment.LaneCells.SelectMany(l => l);

            return nextDir switch
            {
                SegmentDirection.RightDown or SegmentDirection.UpLeft
                    // largest X and Y
                    => cells.MaxBy(c => (c.Position.X, c.Position.Y))!,
                SegmentDirection.RightUp or SegmentDirection.DownLeft
                    // Need largest X and, when equal, smallest Y
                    => cells.MaxBy(c => (c.Position.X, -c.Position.Y))!,

                SegmentDirection.LeftUp
                    // Need smallest X and Y -> negate both to turn “smallest” into “largest”
                    => cells.MaxBy(c => (-c.Position.X, -c.Position.Y))!,

                SegmentDirection.UpRight or SegmentDirection.LeftDown
                    // Need smallest X, largest Y -> negate X only
                    => cells.MaxBy(c => (-c.Position.X, c.Position.Y))!,

                SegmentDirection.DownRight
                    // Same rule as “Left” (min X, min Y)
                    => cells.MaxBy(c => (-c.Position.X, -c.Position.Y))!,

                _ => throw new ArgumentException($"Unsupported direction: {nextDir}")
            };
        }

        private static Point GetNextStartPoint(SegmentDirection nextDirection, Cell lastCell)
        {
            return nextDirection switch
            {
                SegmentDirection.Left or SegmentDirection.LeftUp or SegmentDirection.LeftDown =>
                    new Point(lastCell.Position.X - 1, lastCell.Position.Y),
                SegmentDirection.Right or SegmentDirection.RightUp or SegmentDirection.RightDown =>
                    new Point(lastCell.Position.X + 1, lastCell.Position.Y),
                SegmentDirection.Up or SegmentDirection.UpRight or SegmentDirection.UpLeft =>
                    new Point(lastCell.Position.X, lastCell.Position.Y + 1),
                SegmentDirection.Down or SegmentDirection.DownRight or SegmentDirection.DownLeft =>
                    new Point(lastCell.Position.X, lastCell.Position.Y - 1),
                _ => throw new ArgumentException($"Unsupported direction: {nextDirection}")
            };
        }

        private static bool IsFirstLaneCurve(SegmentDirection dir)
        {
            if (IsBasic(dir))
            {
                throw new ArgumentException($"Direction {dir} is not a curve direction");
            }
            else
            {
                return dir switch
                {
                    SegmentDirection.UpRight or SegmentDirection.RightUp or SegmentDirection.DownRight or SegmentDirection.LeftUp => true,
                    SegmentDirection.UpLeft or SegmentDirection.RightDown or SegmentDirection.DownLeft or SegmentDirection.LeftDown => false,
                    _ => throw new ArgumentException($"Unsupported direction: {dir}")
                };
            }
        }
    }
}
