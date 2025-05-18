using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class LeaveRoomRequest
    {
        [Required]
        public required string RoomId { get; set; }
    }
}