using System.ComponentModel.DataAnnotations;
using Toko.Shared.Models;
using Toko.Shared.Validation;

namespace Toko.Models.Requests
{
    public class CreateRoomRequest : IValidatableObject
    {
        [Required]
        [PlayerName]
        public required string PlayerName { get; set; }
        [MinLength(1, ErrorMessage = "Room name must be at least 1 character long.")]
        public string? RoomName { get; set; }
        [Range(4, 8, ErrorMessage = "Max players must be between 4 and 8.")]
        public int MaxPlayers { get; set; } = 8;
        public bool IsPrivate { get; set; } = false;
        //[Range(1, 20)]
        //public int TotalRounds { get; set; } = 3;
        [Required]
        [MinLength(1, ErrorMessage = "StepsPerRound must have at least 1 value.")]
        public List<int> StepsPerRound { get; set; } = [3, 3, 3];
        public CustomMapRequest? CustomMap { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            // Every step value must be >= 1
            if (StepsPerRound.Any(s => s < 1))
            {
                yield return new ValidationResult(
                    "Each value in StepsPerRound must be at least 1.",
                    new[] { nameof(StepsPerRound) });
            }

            if (RoomName is not null && RoomName.Trim().Length < 1)
            {
                yield return new ValidationResult(
                    "RoomName must be at least 1 character long.",
                    new[] { nameof(RoomName) });
            }

            // Validate CustomMap if provided
            if (CustomMap?.Segments != null)
            {
                var segments = CustomMap.Segments;

                // Check if segments list is empty
                if (segments.Count == 0)
                {
                    yield return new ValidationResult(
                        "CustomMap segments cannot be empty. At least one segment is required.",
                        new[] { nameof(CustomMap) });
                    yield break; // No need to check further if there are no segments
                }

                // Limit segment count to prevent DoS
                const int MaxSegments = 50;
                if (segments.Count > MaxSegments)
                {
                    yield return new ValidationResult(
                        $"Too many segments. Maximum allowed is {MaxSegments}.",
                        new[] { nameof(CustomMap) });
                    yield break;
                }

                const int MaxLaneCount = 10;
                const int MaxCellCount = 20;
                const int MaxTotalCells = 500;
                int totalCells = 0;

                for (int i = 0; i < segments.Count; i++)
                {
                    var seg = segments[i];

                    // Validate enum values
                    if (!Enum.TryParse<SegmentDirection>(seg.Direction, out _))
                    {
                        yield return new ValidationResult(
                            $"Invalid direction '{seg.Direction}' at segment {i}.",
                            new[] { nameof(CustomMap) });
                    }

                    if (!Enum.TryParse<CellType>(seg.Type, out _))
                    {
                        yield return new ValidationResult(
                            $"Invalid type '{seg.Type}' at segment {i}.",
                            new[] { nameof(CustomMap) });
                    }

                    if (seg.LaneCount < 1 || seg.LaneCount > MaxLaneCount)
                    {
                        yield return new ValidationResult(
                            $"LaneCount must be between 1 and {MaxLaneCount}. Got {seg.LaneCount} at segment {i}.",
                            new[] { nameof(CustomMap) });
                    }

                    if (seg.CellCount < 1 || seg.CellCount > MaxCellCount)
                    {
                        yield return new ValidationResult(
                            $"CellCount must be between 1 and {MaxCellCount}. Got {seg.CellCount} at segment {i}.",
                            new[] { nameof(CustomMap) });
                    }

                    totalCells += seg.LaneCount * seg.CellCount;
                }

                // Limit total map size to prevent memory exhaustion
                if (totalCells > MaxTotalCells)
                {
                    yield return new ValidationResult(
                        $"Map too large. Total cells ({totalCells}) exceeds maximum ({MaxTotalCells}).",
                        new[] { nameof(CustomMap) });
                }

                // Check for adjacent segments
                for (int i = 0; i < segments.Count; i++)
                {
                    var currentSegment = segments[i];
                    var nextSegment = segments[(i + 1) % segments.Count]; // wrap around to first segment

                    // Check if adjacent segments have opposite directions
                    if (AreDirectionsOpposite(currentSegment.Direction, nextSegment.Direction))
                    {
                        yield return new ValidationResult(
                            $"Adjacent segments cannot have opposite directions. Found '{currentSegment.Direction}' followed by '{nextSegment.Direction}' at positions {i} and {(i + 1) % segments.Count}.",
                            new[] { nameof(CustomMap) });
                    }

                    // Check if adjacent segments are completely identical (same direction and lane count)
                    if (currentSegment.Direction == nextSegment.Direction && currentSegment.LaneCount == nextSegment.LaneCount)
                    {
                        yield return new ValidationResult(
                            $"Adjacent segments cannot be identical. Found segments with same direction '{currentSegment.Direction}' and lane count {currentSegment.LaneCount} at positions {i} and {(i + 1) % segments.Count}.",
                            new[] { nameof(CustomMap) });
                    }
                }
            }
        }

        private static bool AreDirectionsOpposite(string direction1, string direction2)
        {
            var oppositeDirections = new Dictionary<string, string>
            {
                { "Left", "Right" },
                { "Right", "Left" },
                { "Up", "Down" },
                { "Down", "Up" },
                { "UpRight", "DownLeft" },
                { "DownLeft", "UpRight" },
                { "UpLeft", "DownRight" },
                { "DownRight", "UpLeft" },
                { "RightUp", "LeftDown" },
                { "LeftDown", "RightUp" },
                { "RightDown", "LeftUp" },
                { "LeftUp", "RightDown" }
            };

            return oppositeDirections.TryGetValue(direction1, out var opposite) && opposite == direction2;
        }
    }
}
