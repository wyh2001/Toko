using System.ComponentModel.DataAnnotations;

namespace Toko.Controllers
{
    public class StartRoomRequest
    {
        [Required]
        public required string RoomId { get; set; }
    }
}