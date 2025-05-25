using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Toko.Filters
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class IdempotentAttribute : Attribute, IFilterMetadata
    {
        public TimeSpan Ttl { get; }
        public int MaxBodyBytes { get; }

        public IdempotentAttribute(int ttlSeconds = 30, int maxBodyBytes = 1_048_576)
        {
            Ttl = TimeSpan.FromSeconds(ttlSeconds);
            MaxBodyBytes = maxBodyBytes;
        }
    }
}
