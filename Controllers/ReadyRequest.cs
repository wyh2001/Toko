using System.ComponentModel.DataAnnotations;

namespace Toko.Controllers
{
    public class ReadyRequest
    {
        [Required]
        public required string RoomId { get; set; }
        [Required]
        public required bool IsReady { get; set; }
    }
}