using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    //public class JoinRoomRequest: IRoomRequest
    public class JoinRoomRequest
    {
        //public required string RoomId { get; set; }
        [Required]
        public required string PlayerName { get; set; }
    }
}
