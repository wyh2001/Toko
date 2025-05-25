using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class StartRoomRequest : IRoomRequest
    {
        [Required]
        public required string RoomId { get; set; }
    }
}