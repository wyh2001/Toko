using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class JoinRoomRequest: IRoomRequest
    {
        [Required]
        public required string RoomId { get; set; }
        [Required]
        public required string PlayerName { get; set; }
    }
}
