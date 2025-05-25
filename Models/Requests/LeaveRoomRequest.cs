using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class LeaveRoomRequest: IRoomRequest
    {
        [Required]
        public required string RoomId { get; set; }
    }
}