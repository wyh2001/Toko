using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class DiscardRequest
    {
        [Required]
        public required string RoomId { get; set; }
        //[Required]
        //public int Step { get; set; }
        [Required]
        public List<string> CardIds { get; set; } = new List<string>();
    }
}