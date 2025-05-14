using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class SubmitExecParamRequest
    {
        [Required]
        public required string RoomId { get; set; }
        [Required]
        public required string PlayerId { get; set; }
        //[Required]
        //public int Step { get; set; }
        [Required]
        public ExecParameter ExecParameter { get; set; } = new();
        //public InstructionType InstructionType { get; set; }

        //public int Parameter { get; set; } = -1;
        //public List<string> DiscardedCardIds { get; set; } = new();
    }
}
