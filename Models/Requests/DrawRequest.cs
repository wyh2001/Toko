using System.ComponentModel.DataAnnotations;

namespace Toko.Models.Requests
{
    public class DrawRequest
    {
        [Required]
        public string RoomId { get; set; }
        [Required]
        public string PlayerId { get; set; }
        [Required]
        public int Count { get; set; } = 3;  // 默认每回合抽牌上限
    }
}

