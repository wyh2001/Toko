using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Toko.Models.Requests
{
    public class UpdateRoomSettingsRequest
    {
        public string? RoomName { get; set; }
        [Range(4, 8, ErrorMessage = "Max players must be between 4 and 8.")]
        public int? MaxPlayers { get; set; }
        public bool? IsPrivate { get; set; }
        [MinLength(1, ErrorMessage = "StepsPerRound must have at least 1 value.")]
        public List<int>? StepsPerRound { get; set; }
        [JsonIgnore]
        public bool IsEmpty => RoomName == null && !MaxPlayers.HasValue && !IsPrivate.HasValue && StepsPerRound == null;

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            // Every step value must be >= 1
            if (StepsPerRound is not null && StepsPerRound.Any(s => s < 1))
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
