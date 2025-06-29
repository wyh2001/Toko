using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Toko.Filters
{
    public class IdempotencyFilter(IMemoryCache cache, ILogger<IdempotencyFilter> log) : IAsyncActionFilter
    {
        private readonly IMemoryCache _cache = cache;
        private readonly ILogger<IdempotencyFilter> _log = log;

        private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(30);
        private const int DefaultMaxBytes = 1_048_576;

        public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
        {
            var http = ctx.HttpContext;
            var req = http.Request;
            var resp = http.Response;

            if (!req.Headers.TryGetValue("Idempotency-Key", out StringValues keyVal) ||
                StringValues.IsNullOrEmpty(keyVal))
            {
                await next();
                return;
            }

            string userId = "anon";
            var user = http.User;
            if (user?.Identity?.IsAuthenticated == true)
            {
                userId = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? "unk";
            }

            // create a unique cache key based on method, path, userId and Idempotency-Key
            var cacheKey = $"{req.Method}:{req.Path}:{userId}:{keyVal}";

            var idempAttr = ctx.ActionDescriptor
                               .EndpointMetadata
                               .OfType<IdempotentAttribute>()
                               .FirstOrDefault();

            var ttl = idempAttr?.Ttl ?? DefaultTtl;
            var maxBytes = idempAttr?.MaxBodyBytes ?? DefaultMaxBytes;

            // cache hit
            if (_cache.TryGetValue(cacheKey, out CachedResponse? entry) && entry != null)
            {
                _log.LogDebug("Idempotency HIT: {Key}", cacheKey);

                resp.StatusCode = entry.StatusCode;
                if (!string.IsNullOrEmpty(entry.ContentType))
                    resp.ContentType = entry.ContentType;

                await resp.Body.WriteAsync(entry.Body);
                await resp.Body.FlushAsync();
                return;
            }

            // cache miss
            var originalBody = resp.Body;
            await using var buffer = new MemoryStream();
            resp.Body = buffer;

            try
            {
                var executed = await next();                    // execute the action

                // only cache successful responses
                if (executed.Exception == null && !executed.Canceled)
                {
                    // do not cache if too large
                    if (buffer.Length > maxBytes)
                    {
                        _log.LogInformation(
                            "Idempotency SKIP (body {Size} > {Max}) for {Key}",
                            buffer.Length, maxBytes, cacheKey);
                    }
                    else
                    {
                        buffer.Position = 0;
                        var bytes = buffer.ToArray();

                        _cache.Set(cacheKey,
                            new CachedResponse(resp.StatusCode,
                                               resp.ContentType,
                                               bytes),
                            ttl);

                        _log.LogDebug("Idempotency STORE: {Key} ({Size} bytes, TTL={Ttl}s)",
                                      cacheKey, bytes.Length, ttl.TotalSeconds);
                    }
                }

                // write the response back to the original body
                buffer.Position = 0;
                await buffer.CopyToAsync(originalBody);
                await originalBody.FlushAsync();
            }
            finally
            {
                resp.Body = originalBody; // restore the original body if something goes wrong
            }
        }
    }
}