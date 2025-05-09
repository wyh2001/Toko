using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Toko.Models;
using Toko.Services;

namespace Toko.Filters
{
    public class EnsureRoomStatusAttribute : TypeFilterAttribute
    {
        public EnsureRoomStatusAttribute(RoomStatus requiredStatus)
            : base(typeof(EnsureRoomStatusFilter))
        {
            // 将所需状态传给过滤器
            Arguments = new object[] { requiredStatus };
        }
    }
}
