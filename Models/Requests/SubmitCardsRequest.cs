using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class SubmitCardsRequest
    {
        [Required]
        public string RoomId { get; set; }
        [Required]
        public string PlayerId { get; set; }
        [Required]
        public List<string> CardIds { get; set; } = new();
    }
}

