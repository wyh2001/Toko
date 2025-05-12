using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class JoinRoomRequest
    {
        [Required]
        public string RoomId { get; set; }
        [Required]
        public string PlayerName { get; set; }
    }
}
