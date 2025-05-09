using System.ComponentModel.DataAnnotations;

namespace Toko.Models
{
    public class CreateRoomRequest
    {
        [Required]
        public string PlayerName { get; set; }
        public string? RoomName { get; set; }
        [Range(4, 8, ErrorMessage = "Max players must be between 4 and 8.")]
        public int MaxPlayers { get; set; } = 8;
        public bool IsPrivate { get; set; } = false;
    }
}
