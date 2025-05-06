using System.ComponentModel.DataAnnotations;

namespace Toko.Models
{
    public class JoinRoomRequest
    {
        [Required]
        public string RoomId { get; set; }
        [Required]
        public string PlayerName { get; set; }
    }
}
