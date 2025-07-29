using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using Toko.Shared.Validation;

namespace Toko.Models.Requests
{
    //public class JoinRoomRequest: IRoomRequest
    public class JoinRoomRequest
    {
        //public required string RoomId { get; set; }
        [Required]
        [PlayerName]
        public required string PlayerName { get; set; }
    }
}
