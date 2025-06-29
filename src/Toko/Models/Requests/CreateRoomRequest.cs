using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class CreateRoomRequest : IValidatableObject
    {
        [Required]
        [MinLength(1)]
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
        }
    }
}
