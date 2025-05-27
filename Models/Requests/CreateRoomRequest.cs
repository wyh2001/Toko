using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class CreateRoomRequest : IValidatableObject
    {
        [Required]
        public required string PlayerName { get; set; }
        public string? RoomName { get; set; }
        [Range(4, 8, ErrorMessage = "Max players must be between 4 and 8.")]
        public int MaxPlayers { get; set; } = 8;
        public bool IsPrivate { get; set; } = false;
        [Range(1, 20)]
        public int TotalRounds { get; set; } = 3;
        [Required]
        public List<int> StepsPerRound { get; set; } = new List<int> { 3, 3, 3 };

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            // Length must match TotalRounds
            if (StepsPerRound.Count != TotalRounds)
            {
                yield return new ValidationResult(
                    $"StepsPerRound length ({StepsPerRound.Count}) must equal TotalRounds ({TotalRounds}).",
                    new[] { nameof(StepsPerRound) });
            }

            // Every step value must be >= 1
            if (StepsPerRound.Any(s => s < 1))
            {
                yield return new ValidationResult(
                    "Each value in StepsPerRound must be at least 1.",
                    new[] { nameof(StepsPerRound) });
            }
        }
    }
}
