using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class DiscardRequest
    {
        [Required]
        public string RoomId { get; set; } = string.Empty;
        [Required]
        public string PlayerId { get; set; } = string.Empty;
        [Required]
        public int Step { get; set; }
        [Required]
        public List<string> CardIds { get; set; } = new List<string>();
    }
}