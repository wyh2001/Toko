using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class SubmitCardsRequest
    {
        [Required]
        public required string RoomId { get; set; }
        [Required]
        public required string PlayerId { get; set; }
        [Required]
        public List<string> CardIds { get; set; } = new();
    }
}

