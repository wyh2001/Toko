using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class ReadyRequest : IRoomRequest
    {
        [Required]
        public required string RoomId { get; set; }
        [Required]
        public required bool IsReady { get; set; }
    }
}