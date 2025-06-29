using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class ReadyRequest
    {
        [Required]
        public required bool IsReady { get; set; }
    }
}