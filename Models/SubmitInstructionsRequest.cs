using System.ComponentModel.DataAnnotations;

namespace Toko.Models
{
    public class SubmitInstructionsRequest
    {
        [Required]
        public string RoomId { get; set; }

        [Required]
        public string PlayerId { get; set; }

        [Required]
        [MinLength(1)]
        public List<InstructionType> Instructions { get; set; } = new();
    }
}
