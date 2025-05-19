using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;

namespace Toko.Filters
{
    public class IdempotencyFilter : IAsyncActionFilter
    {
        private readonly IMemoryCache _cache;
        //private readonly ILogger<IdempotencyFilter> _logger;
        private static readonly TimeSpan TTL = TimeSpan.FromMinutes(2);

        public IdempotencyFilter(IMemoryCache cache, ILogger<IdempotencyFilter> logger)
        {
            _cache = cache;
            //_logger = logger;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var key = context.HttpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();

            // 没带 key —— 直接放行
            if (string.IsNullOrWhiteSpace(key))
            {
                await next();
                return;
            }

            // 查缓存
            if (_cache.TryGetValue<IActionResult>(key, out var cached))
            {
                //_logger.LogDebug("Idempotency hit: {Key}", key);
                context.Result = cached;     // 短路：直接用上次的结果
                return;
            }

            // 首次出现 —— 正常执行
            var executed = await next();     // 执行真正的 Action

            if (executed.Exception is null)  // 成功才缓存，异常不要缓存
            {
                _cache.Set(key, executed.Result!, TTL);
                //_logger.LogDebug("Idempotency store: {Key}", key);
            }
        }
    }

}
