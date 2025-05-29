using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Toko.Filters
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class IdempotentAttribute(int ttlSeconds = 30, int maxBodyBytes = 1_048_576) : Attribute, IFilterMetadata
    {
        public TimeSpan Ttl { get; } = TimeSpan.FromSeconds(ttlSeconds);
        public int MaxBodyBytes { get; } = maxBodyBytes;
    }
}
