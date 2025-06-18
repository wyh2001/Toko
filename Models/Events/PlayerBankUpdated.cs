using MediatR;
using System;

namespace Toko.Models.Events
{
    public record PlayerBankUpdated(string RoomId, string PlayerId, TimeSpan BankTime) : IRoomEvent;
}
