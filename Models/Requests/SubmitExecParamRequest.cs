using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class SubmitExecParamRequest : IRoomRequest
    {
        [Required]
        public required string RoomId { get; set; }
        [Required]
        public required ExecParameter ExecParameter { get; set; }
    }
}
