using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class KickPlayerRequest
    {
        [Required]
        public required string KickedPlayerId { get; set; }
    }
}
