using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Toko.Models;
using Toko.Services;
using static Toko.Models.Room;

namespace Toko.Filters
{
    public class EnsureRoomStatusAttribute : TypeFilterAttribute
    {
        public EnsureRoomStatusAttribute(RoomStatus requiredStatus)
            : base(typeof(EnsureRoomStatusFilter))
        {
            // Pass the required status to the filter
            Arguments = new object[] { requiredStatus };
        }
    }
}
