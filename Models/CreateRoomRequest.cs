using System.ComponentModel.DataAnnotations;

namespace Toko.Models
{
    public class CreateRoomRequest
    {
        [Required]
        public string PlayerName { get; set; }

        public string? RoomName { get; set; }
        public int MaxPlayers { get; set; } = 8;
        public bool IsPrivate { get; set; } = false;
    }
}
