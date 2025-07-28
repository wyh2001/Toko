using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using MediatR;
using Toko.Models;
using Toko.Models.Events;
using Toko.Shared.Models;
using static Toko.Shared.Services.RaceMapFactory;

namespace Toko.Services
{
    public class TurnExecutor(RaceMap map, ILogger<TurnExecutor> log)
    {
        private readonly RaceMap _map = map ?? throw new ArgumentNullException(nameof(map));
        private readonly ILogger<TurnExecutor> _log = log;
        private const int MAX_INTERACTION_DEPTH = 5;

        // Coordinate tracking system
        private readonly ConcurrentDictionary<Point, TrackPosition> _coordinateMap = new();
        private readonly ConcurrentDictionary<TrackPosition, Point> _positionToCoordinate = new();
        private readonly ConcurrentDictionary<Point, Point?> _nextCoordinateMap = new();
        private volatile bool _coordinateSystemInitialized = false;

        // Track whether each racer has left the starting segment at least once
        private readonly ConcurrentDictionary<string, bool> _racerHasLeftStartingSegment = new();

        public enum TurnExecutionResult
        {
            Continue,
            PlayerFinished,
            InvalidState
        }

        // Structure for representing track position
        public struct TrackPosition
        {
            public int SegmentIndex;
            public int LaneIndex;
            public int CellIndex;

            public TrackPosition(int segmentIndex, int laneIndex, int cellIndex)
            {
                SegmentIndex = segmentIndex;
                LaneIndex = laneIndex;
                CellIndex = cellIndex;
            }

            public override bool Equals(object? obj)
            {
                return obj is TrackPosition position &&
                       SegmentIndex == position.SegmentIndex &&
                       LaneIndex == position.LaneIndex &&
                       CellIndex == position.CellIndex;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(SegmentIndex, LaneIndex, CellIndex);
            }
        }

        private void InitializeCoordinateSystem()
        {
            if (_coordinateSystemInitialized) return;

            // 1. Build coordinate to position mapping
            BuildCoordinateMap();

            // 2. Find the next forward position for each coordinate
            BuildNextCoordinateMap();

            _coordinateSystemInitialized = true;
        }

        private void BuildCoordinateMap()
        {
            _coordinateMap.Clear();
            _positionToCoordinate.Clear();

            for (int segIndex = 0; segIndex < _map.Segments.Count; segIndex++)
            {
                var segment = _map.Segments[segIndex];
                for (int laneIndex = 0; laneIndex < segment.LaneCount; laneIndex++)
                {
                    for (int cellIndex = 0; cellIndex < segment.CellCount; cellIndex++)
                    {
                        var cell = segment.LaneCells[laneIndex][cellIndex];
                        var position = new TrackPosition(segIndex, laneIndex, cellIndex);

                        _coordinateMap[cell.Position] = position;
                        _positionToCoordinate[position] = cell.Position;
                    }
                }
            }
        }

        private void BuildNextCoordinateMap()
        {
            _nextCoordinateMap.Clear();

            // Get all unique coordinates and sort them in ring structure
            var allCoordinates = _coordinateMap.Keys.ToHashSet();
            var coordinateRings = BuildCoordinateRings(allCoordinates);

            // Establish next relationship for each ring
            foreach (var ring in coordinateRings)
            {
                for (int i = 0; i < ring.Count; i++)
                {
                    var current = ring[i];
                    var next = ring[(i + 1) % ring.Count]; // Circular, last points to first
                    _nextCoordinateMap[current] = next;
                }
            }
        }

        private List<List<Point>> BuildCoordinateRings(HashSet<Point> allCoordinates)
        {
            var rings = new List<List<Point>>();
            var minX = allCoordinates.Min(p => p.X);
            var minY = allCoordinates.Min(p => p.Y);

            return ScanBoundaryWithCornerDetection(allCoordinates, minX, minY);
        }

        private List<List<Point>> ScanBoundaryWithCornerDetection(HashSet<Point> allCoordinates,
            int minX, int minY)
        {
            var processed = new HashSet<Point>();
            var ring = new List<Point>();
            var rings = new List<List<Point>>();
            Point startPoint = new(minX, minY);
            Point nextPoint = startPoint;
            var counter = 0;
            var maxCount = int.MaxValue;
            var isFirstHalf = false;
            while (true)
            {
                if (!_coordinateMap.TryGetValue(nextPoint, out var trackPosition))
                {
                    throw new InvalidOperationException($"Cannot find track position for coordinate {nextPoint}");
                    // rings.Add(ring);
                    // return rings;
                }
                var seg = _map.Segments[trackPosition.SegmentIndex];
                var direction = seg.Direction;
                var isCorner = seg.IsCorner;
                if (nextPoint == startPoint && processed.Contains(nextPoint))
                {
                    rings.Add(ring);
                    ring = new List<Point>();
                    // Advance start to next inner layer; break if no further coordinates
                    startPoint = new Point(startPoint.X + 1, startPoint.Y + 1);
                    if (!allCoordinates.Contains(startPoint))
                        break; // No more layers
                    nextPoint = startPoint;
                    processed.Clear();
                    continue;
                }
                if (!isCorner)
                {
                    counter = 0;
                    isFirstHalf = true;
                    ring.Add(nextPoint);
                    processed.Add(nextPoint);
                    nextPoint = GetNextPoint(nextPoint, direction);
                }
                else
                {
                    counter++;
                    var isFirstLaneCurve = IsFirstLaneCurve(direction);
                    maxCount = isFirstLaneCurve ? seg.CellCount - trackPosition.LaneIndex : trackPosition.LaneIndex + 1;
                    if (counter >= maxCount)
                    {
                        isFirstHalf = false;
                    }
                    switch (direction)
                    {
                        case SegmentDirection.UpRight:
                            // Add current point and move to next corner
                            ring.Add(nextPoint);
                            processed.Add(nextPoint);
                            nextPoint = GetNextPoint(nextPoint, isFirstHalf ? SegmentDirection.Up : SegmentDirection.Right);
                            break;
                        case SegmentDirection.UpLeft:
                            // Add current point and move to next corner
                            ring.Add(nextPoint);
                            processed.Add(nextPoint);
                            nextPoint = GetNextPoint(nextPoint, isFirstHalf ? SegmentDirection.Up : SegmentDirection.Left);
                            break;
                        case SegmentDirection.DownRight:
                            ring.Add(nextPoint);
                            processed.Add(nextPoint);
                            nextPoint = GetNextPoint(nextPoint, isFirstHalf ? SegmentDirection.Down : SegmentDirection.Right);
                            break;
                        case SegmentDirection.DownLeft:
                            ring.Add(nextPoint);
                            processed.Add(nextPoint);
                            nextPoint = GetNextPoint(nextPoint, isFirstHalf ? SegmentDirection.Down : SegmentDirection.Left);
                            break;
                        case SegmentDirection.RightUp:
                            ring.Add(nextPoint);
                            processed.Add(nextPoint);
                            nextPoint = GetNextPoint(nextPoint, isFirstHalf ? SegmentDirection.Right : SegmentDirection.Up);
                            break;
                        case SegmentDirection.RightDown:
                            ring.Add(nextPoint);
                            processed.Add(nextPoint);
                            nextPoint = GetNextPoint(nextPoint, isFirstHalf ? SegmentDirection.Right : SegmentDirection.Down);
                            break;
                        case SegmentDirection.LeftUp:
                            ring.Add(nextPoint);
                            processed.Add(nextPoint);
                            nextPoint = GetNextPoint(nextPoint, isFirstHalf ? SegmentDirection.Left : SegmentDirection.Up);
                            break;
                        case SegmentDirection.LeftDown:
                            ring.Add(nextPoint);
                            processed.Add(nextPoint);
                            nextPoint = GetNextPoint(nextPoint, isFirstHalf ? SegmentDirection.Left : SegmentDirection.Down);
                            break;
                        default:
                            throw new InvalidOperationException($"Unsupported corner segment direction: {direction}");
                    }
                }
            }

            return rings;
        }

        private Point GetNextPoint(Point current, SegmentDirection direction)
        {
            return direction switch
            {
                SegmentDirection.Left => new Point(current.X - 1, current.Y),
                SegmentDirection.Right => new Point(current.X + 1, current.Y),
                SegmentDirection.Up => new Point(current.X, current.Y + 1),
                SegmentDirection.Down => new Point(current.X, current.Y - 1),
                _ => throw new InvalidOperationException($"Unsupported segment direction: {direction}")
            };
        }

        private SegmentDirection GetOppositeDirection(SegmentDirection direction)
        {
            return direction switch
            {
                SegmentDirection.Left => SegmentDirection.Right,
                SegmentDirection.Right => SegmentDirection.Left,
                SegmentDirection.Up => SegmentDirection.Down,
                SegmentDirection.Down => SegmentDirection.Up,
                _ => throw new InvalidOperationException($"Unsupported segment direction: {direction}")
            };
        }

        private bool IsCellInCornerSegment(Point coordinate)
        {
            if (_coordinateMap.TryGetValue(coordinate, out var position))
            {
                if (position.SegmentIndex >= 0 && position.SegmentIndex < _map.Segments.Count)
                {
                    var segment = _map.Segments[position.SegmentIndex];
                    return segment.IsIntermediate; // IsIntermediate means it's a corner
                }
            }
            return false;
        }

        private (int x, int y) GetNextPosition(int currentX, int currentY, int direction)
        {
            return direction switch
            {
                0 => (currentX + 1, currentY),     // Right
                1 => (currentX, currentY - 1),     // Down
                2 => (currentX - 1, currentY),     // Left
                3 => (currentX, currentY + 1),     // Up
                _ => (currentX, currentY)
            };
        }

        public TurnExecutionResult ApplyInstruction(Racer racer, ConcreteInstruction ins, Room room, List<INotification> events)
        {
            // Ensure coordinate system is initialized
            InitializeCoordinateSystem();

            // Execute action first
            switch (ins.Type)
            {
                case CardType.ChangeLane:
                    if (ins.ExecParameter.Effect != -1 && ins.ExecParameter.Effect != 1)
                    {
                        return TurnExecutionResult.InvalidState;
                    }
                    ChangeLaneNew(racer, ins.ExecParameter.Effect, room, events, 0); // Initial depth 0
                    return TurnExecutionResult.Continue;
                case CardType.Repair:
                    Repair(racer, ins.ExecParameter.DiscardedCardIds);
                    return TurnExecutionResult.Continue;
                case CardType.ShiftGear:
                    if (ins.ExecParameter.Effect != -1 && ins.ExecParameter.Effect != 1)
                    {
                        return TurnExecutionResult.InvalidState;
                    }
                    ShiftGear(racer, ins.ExecParameter.Effect, room, events); // Use parameter: 1 for shift up, -1 for shift down
                    return TurnExecutionResult.Continue;
                default:
                    // Junk won't be submitted here
                    return TurnExecutionResult.Continue;
            }
        }

        private TurnExecutionResult MoveForwardNew(Racer racer, int steps, Room room, List<INotification> events, int depth)
        {
            if (depth >= MAX_INTERACTION_DEPTH)
            {
                _log.LogWarning($"MoveForward for racer {racer.Id} reached max interaction depth of {MAX_INTERACTION_DEPTH}. Halting further movement in this step to prevent potential loop.");
                return TurnExecutionResult.Continue;
            }

            var currentPosition = new TrackPosition(racer.SegmentIndex, racer.LaneIndex, racer.CellIndex);

            // Store initial position for event generation
            int initialSegment = racer.SegmentIndex;
            int initialLane = racer.LaneIndex;
            int initialCell = racer.CellIndex;

            // Track corner pass-through for gear limit violation detection
            bool passedThroughCorner = false;
            int cornerGearLimit = 6; // Default gear limit

            for (int i = 0; i < steps; i++)
            {
                // Get current position coordinates
                if (!_positionToCoordinate.TryGetValue(currentPosition, out var currentCoordinate))
                {
                    _log.LogError($"Cannot find coordinate for position {currentPosition.SegmentIndex}-{currentPosition.LaneIndex}-{currentPosition.CellIndex}");
                    return TurnExecutionResult.InvalidState;
                }

                // Get next coordinate
                if (!_nextCoordinateMap.TryGetValue(currentCoordinate, out var nextCoordinate) || nextCoordinate == null)
                {
                    // If no next coordinate, reached finish line
                    return TurnExecutionResult.PlayerFinished;
                }

                // Convert next coordinate to position
                if (!_coordinateMap.TryGetValue(nextCoordinate.Value, out var nextPosition))
                {
                    _log.LogError($"Cannot find position for coordinate {nextCoordinate.Value}");
                    return TurnExecutionResult.InvalidState;
                }

                // Collision check: check if next position is occupied before moving
                var collided = room.Racers
                    .Where(r => r.Id != racer.Id
                             && r.SegmentIndex == nextPosition.SegmentIndex
                             && r.CellIndex == nextPosition.CellIndex
                             && r.LaneIndex == nextPosition.LaneIndex)
                    .ToList();

                if (collided.Count != 0)
                {
                    // Generate collision event
                    events.Add(new PlayerCollision(
                        room.Id,
                        room.CurrentRound,
                        room.CurrentStep,
                        racer.Id,
                        racer.PlayerName,
                        collided.Select(r => r.Id).ToList(),
                        collided.Select(r => r.PlayerName).ToList(),
                        nextPosition.SegmentIndex,
                        nextPosition.LaneIndex,
                        nextPosition.CellIndex
                    ));

                    AddJunk(racer, 2);
                    DownshiftGear(racer, 2);
                    foreach (var other in collided)
                    {
                        AddJunk(other, 1);
                        DownshiftGear(other, 1);
                    }
                    
                    // Stop further movement for the current racer in this turn
                    break; 
                }

                // Update racer position
                racer.SegmentIndex = nextPosition.SegmentIndex;
                racer.LaneIndex = nextPosition.LaneIndex;
                racer.CellIndex = nextPosition.CellIndex;
                currentPosition = nextPosition;

                racer.Score++;

                // Track corner pass-through: if we moved through a corner segment
                var currentSegment = _map.Segments[racer.SegmentIndex];
                if (currentSegment.IsCorner && !passedThroughCorner)
                {
                    passedThroughCorner = true;
                    cornerGearLimit = 4;
                }

                // Track if the racer has left the starting segment
                if (racer.SegmentIndex != 0)
                {
                    _racerHasLeftStartingSegment[racer.Id] = true;
                }

                // Check for special grid types that require automatic forward movement
                var currentCell = _map.Segments[racer.SegmentIndex].LaneCells[racer.LaneIndex][racer.CellIndex];
                if (currentCell.Grid?.RenderingType == MapRenderingType.CurveLargeSeg2)
                {
                    _log.LogDebug($"Racer {racer.Id} hit CurveLargeSeg2 at position {racer.SegmentIndex}-{racer.LaneIndex}-{racer.CellIndex}, automatically moving forward");

                    // Automatically move forward one more step without counting towards the total steps
                    if (_positionToCoordinate.TryGetValue(currentPosition, out var autoMoveCoordinate) &&
                        _nextCoordinateMap.TryGetValue(autoMoveCoordinate, out var autoMoveNextCoordinate) &&
                        autoMoveNextCoordinate != null &&
                        _coordinateMap.TryGetValue(autoMoveNextCoordinate.Value, out var autoMoveNextPosition))
                    {
                        racer.SegmentIndex = autoMoveNextPosition.SegmentIndex;
                        racer.LaneIndex = autoMoveNextPosition.LaneIndex;
                        racer.CellIndex = autoMoveNextPosition.CellIndex;
                        currentPosition = autoMoveNextPosition;

                        racer.Score++;

                        // Track if the racer has left the starting segment after auto-move
                        if (racer.SegmentIndex != 0)
                        {
                            _racerHasLeftStartingSegment[racer.Id] = true;
                        }
                    }
                }

                // Check if race is finished - when returning to starting point
                if (IsAtFinishLine(racer))
                {
                    return TurnExecutionResult.PlayerFinished;
                }
            }

            // Generate move event if the position actually changed
            if (initialSegment != racer.SegmentIndex || initialLane != racer.LaneIndex || initialCell != racer.CellIndex)
            {
                events.Add(new PlayerMoved(
                    room.Id,
                    room.CurrentRound,
                    room.CurrentStep,
                    racer.Id,
                    racer.PlayerName,
                    steps,
                    initialSegment,
                    initialLane,
                    initialCell,
                    racer.SegmentIndex,
                    racer.LaneIndex,
                    racer.CellIndex
                ));
            }

            // Check for corner pass-through gear limit violation
            // If racer passed through any corner during this move and gear exceeds limit, add junk card
            if (passedThroughCorner)
            {
                if (racer.Gear > cornerGearLimit)
                {
                    AddJunk(racer, 1);
                    _log.LogDebug($"Racer {racer.Id} received junk card for passing through corner at gear {racer.Gear} (limit: {cornerGearLimit})");
                    
                    // Generate corner gear limit violation event
                    events.Add(new PlayerCornerGearLimitViolation(
                        room.Id,
                        room.CurrentRound,
                        room.CurrentStep,
                        racer.Id,
                        racer.PlayerName,
                        racer.Gear,
                        cornerGearLimit
                    ));
                }
            }

            return TurnExecutionResult.Continue;
        }
        private void ChangeLaneNew(Racer racer, int delta, Room room, List<INotification> events, int depth)
        {
            if (depth >= MAX_INTERACTION_DEPTH)
            {
                _log.LogWarning($"ChangeLane for racer {racer.Id} reached max interaction depth of {MAX_INTERACTION_DEPTH}. Halting further lane changes in this step to prevent potential loop.");
                return;
            }

            var currentPosition = new TrackPosition(racer.SegmentIndex, racer.LaneIndex, racer.CellIndex);
            int initialLane = racer.LaneIndex;

            var seg = _map.Segments[racer.SegmentIndex];
            if (seg.IsCorner)
            {
                events.Add(new PlayerCornerLaneChangeFailed(
                    room.Id,
                    room.CurrentRound,
                    room.CurrentStep,
                    racer.Id,
                    racer.PlayerName
                ));
                AddJunk(racer, 1);
                return;
            }

            // Get current coordinate
            if (!_positionToCoordinate.TryGetValue(currentPosition, out var currentCoordinate))
            {
                _log.LogError($"Cannot find coordinate for position {currentPosition.SegmentIndex}-{currentPosition.LaneIndex}-{currentPosition.CellIndex}");
                return;
            }

            // Calculate target lane
            int targetLane = racer.LaneIndex + delta;

            // Boundary check: hitting wall
            if (targetLane < 0 || targetLane >= seg.LaneCount)
            {
                events.Add(new PlayerHitWall(
                    room.Id,
                    room.CurrentRound,
                    room.CurrentStep,
                    racer.Id,
                    racer.PlayerName,
                    delta,
                    racer.LaneIndex
                ));
                AddJunk(racer, 1);
                return;
            }

            // Check for collision in target position
            var collided = room.Racers
                .Where(r => r.Id != racer.Id
                         && r.SegmentIndex == racer.SegmentIndex
                         && r.CellIndex == racer.CellIndex
                         && r.LaneIndex == targetLane)
                .ToList();

            if (collided.Count != 0)
            {
                events.Add(new PlayerLaneChangeBlocked(
                    room.Id,
                    room.CurrentRound,
                    room.CurrentStep,
                    racer.Id,
                    racer.PlayerName,
                    delta,
                    collided.Select(r => r.Id).ToList(),
                    collided.Select(r => r.PlayerName).ToList()
                ));

                // Lane change collision penalty: lane changer gets 1 junk card and downshift 1 gear, blocking car gets 1 junk card and downshift 3 gears
                AddJunk(racer, 1);
                DownshiftGear(racer, 1);
                foreach (var other in collided)
                {
                    AddJunk(other, 1);
                    DownshiftGear(other, 3);
                }
                return; // Stop further lane changes after collision
            }

            // Execute lane change
            racer.LaneIndex = targetLane;

            // Generate successful lane change event
            events.Add(new PlayerChangedLane(
                room.Id,
                room.CurrentRound,
                room.CurrentStep,
                racer.Id,
                racer.PlayerName,
                delta,
                initialLane,
                targetLane,
                true
            ));
        }

        private bool IsAtFinishLine(Racer racer)
        {
            // Check if racer has returned to the starting segment (segment 0) for the second time
            // This means the racer has completed a full lap around the track
            return racer.SegmentIndex == 0 &&
                   _racerHasLeftStartingSegment.GetValueOrDefault(racer.Id, false);
        }

        private static void AddJunk(Racer racer, int qty)
        {
            for (int i = 0; i < qty; i++)
                racer.Deck.Enqueue(new Card { Type = CardType.Junk });
        }

        private static void DownshiftGear(Racer racer, int levels)
        {
            int newGear = racer.Gear - levels;
            
            if (newGear < 1)
            {
                newGear = 1;
            }
            
            racer.Gear = newGear;
        }

        private static void Repair(Racer racer, List<string> discardedCardId)
        {
            // Check if the size is legitimate
            if (discardedCardId.Count > 2)
                return; // or throw?
            // Check if the cards are in the hand
            foreach (var cardId in discardedCardId)
            {
                var card = racer.Hand
                    .FirstOrDefault(c => c.Id == cardId);
                if (card == null)
                    return; // not in hand
                if (card.Type != CardType.Junk)
                    return; // not junk
            }
            // Discard the junk card the user chose, remove directly without adding to discard pile
            InternalRemove(racer, discardedCardId);
            
            // Adjust gear after removing junk cards (may allow higher gears)
            CardHelper.AdjustGearForJunkCards(racer);
        }

        private static void ShiftGear(Racer racer, int direction, Room room, List<INotification> events)
        {
            // direction: 1 for shift up, -1 for shift down
            int oldGear = racer.Gear;
            int newGear = racer.Gear + direction;
            
            // Count junk cards in hand to determine max gear limit
            int junkCardCount = racer.Hand.Count(card => card.Type == CardType.Junk);
            int maxAllowedGear = 6 - junkCardCount; // Each junk card reduces max gear by 1
            
            // Check if player tries to shift up beyond the maximum allowed gear
            bool hitGearLimit = false;
            if (direction > 0 && racer.Gear >= maxAllowedGear)
            {
                hitGearLimit = true;
                AddJunk(racer, 1);
            }
            
            // Clamp gear between 1 and max allowed gear
            if (newGear < 1)
            {
                newGear = 1;
            }
            else if (newGear > maxAllowedGear)
            {
                newGear = maxAllowedGear;
            }
            
            racer.Gear = newGear;

            // Generate gear change event
            events.Add(new PlayerChangedGear(
                room.Id,
                room.CurrentRound,
                room.CurrentStep,
                racer.Id,
                racer.PlayerName,
                direction,
                oldGear,
                newGear
            ));
            
            // Generate gear limit violation event if player hit the limit
            if (hitGearLimit)
            {
                events.Add(new PlayerGearLimitExceeded(
                    room.Id,
                    room.CurrentRound,
                    room.CurrentStep,
                    racer.Id,
                    racer.PlayerName,
                    maxAllowedGear
                ));
            }
        }

        public TurnExecutionResult ExecuteAutoMove(Racer racer, Room room, List<INotification> events)
        {
            return ExecuteAutoMove(racer, room, events, racer.Gear);
        }

        public TurnExecutionResult ExecuteAutoMove(Racer racer, Room room, List<INotification> events, int moveDistance)
        {
            // Ensure coordinate system is initialized
            InitializeCoordinateSystem();

            // Execute movement
            var result = MoveForwardNew(racer, moveDistance, room, events, 0);

            return result;
        }

        private static void InternalRemove(Racer racer, List<string> discardedCardId)
        {
            // Check if the size is legitimate
            if (discardedCardId.Count == 0)
                return;

            foreach (var cardId in discardedCardId)
            {
                var card = racer.Hand
                    .FirstOrDefault(c => c.Id == cardId);
                if (card != null)
                {
                    racer.Hand.Remove(card);
                }
            }
        }

        // Discard cards
        private static void InternalDiscard(Racer racer, List<string> discardedCardId)
        {
            // Check if the size is legitimate
            if (discardedCardId.Count == 0)
                return;

            foreach (var cardId in discardedCardId)
            {
                var card = racer.Hand
                    .FirstOrDefault(c => c.Id == cardId);
                if (card != null)
                {
                    racer.Hand.Remove(card);
                    racer.DiscardPile.Add(card);
                }
            }
        }

        public bool DiscardCards(Racer racer, List<string> discardedCardId)
        {
            if (discardedCardId.Count == 0)
                return true;
            // Check if the cards are in the hand
            foreach (var cardId in discardedCardId)
            {
                var card = racer.Hand
                    .FirstOrDefault(c => c.Id == cardId);
                if (card == null)
                    return false; // not all in hand
            }
            // Discard the cards the user chose
            InternalDiscard(racer, discardedCardId);
            return true;
        }

        public Point? GetCurrentCoordinate(Racer racer)
        {
            var position = new TrackPosition(racer.SegmentIndex, racer.LaneIndex, racer.CellIndex);
            return _positionToCoordinate.TryGetValue(position, out var coordinate) ? coordinate : null;
        }

        public Point? GetNextCoordinate(Racer racer)
        {
            var currentCoord = GetCurrentCoordinate(racer);
            if (currentCoord == null) return null;

            return _nextCoordinateMap.TryGetValue(currentCoord.Value, out var next) ? next : null;
        }
    }
}
