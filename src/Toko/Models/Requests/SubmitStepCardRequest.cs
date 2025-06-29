using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class SubmitStepCardRequest
    {
        //[Required]
        //public required string RoomId { get; set; }
        [Required]
        public required string CardId { get; set; }
    }
}
