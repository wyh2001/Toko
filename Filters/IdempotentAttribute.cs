using Microsoft.AspNetCore.Mvc;

namespace Toko.Filters
{
    [AttributeUsage(AttributeTargets.Method)]
    public class IdempotentAttribute : TypeFilterAttribute
    {
        public IdempotentAttribute() : base(typeof(IdempotencyFilter)) { }
    }
}
