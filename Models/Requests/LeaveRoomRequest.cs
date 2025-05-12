using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class LeaveRoomRequest
    {
        [Required]
        public string RoomId { get; set; }
        [Required]
        public string PlayerId { get; set; }
    }
}