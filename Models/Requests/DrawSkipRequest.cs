using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class DrawSkipRequest : IRoomRequest
    {
        [Required]
        public required string RoomId { get; set; }
    }
}

