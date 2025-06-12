using MediatR;
using System;

namespace Toko.Models.Events
{
    public class PlayerBankUpdated(string roomId, string playerId, TimeSpan bankTime) : IRoomEvent
    {
        public string RoomId { get; } = roomId;
        public string PlayerId { get; } = playerId;
        public TimeSpan BankTime { get; } = bankTime;
    }
}
