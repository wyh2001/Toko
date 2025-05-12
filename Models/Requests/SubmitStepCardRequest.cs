namespace Toko.Models.Requests
{
    public class SubmitStepCardRequest
    {
        public string RoomId { get; set; }
        public string PlayerId { get; set; }
        public int Step { get; set; }
        public string CardId { get; set; }
    }
}
