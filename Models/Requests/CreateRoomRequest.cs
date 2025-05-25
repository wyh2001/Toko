using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class CreateRoomRequest
    {
        [Required]
        public required string PlayerName { get; set; }
        public string? RoomName { get; set; }
        [Range(4, 8, ErrorMessage = "Max players must be between 4 and 8.")]
        public int MaxPlayers { get; set; } = 8;
        public bool IsPrivate { get; set; } = false;
        [Range(1, 20)]
        public int TotalRounds { get; set; } = 3;
        /// <summary>每轮步数，长度应当 = TotalRounds</summary>
        [Required]
        public List<int> StepsPerRound { get; set; } = new List<int> { 3, 3, 3 };
    }
}
