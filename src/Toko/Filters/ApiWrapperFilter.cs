using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;
using static Toko.Controllers.RoomController;

namespace Toko.Filters
{
    public class ApiWrapperFilter : IAsyncResultFilter
    {
        private static readonly HashSet<int> SuccessCodes =
            [StatusCodes.Status200OK, StatusCodes.Status201Created, StatusCodes.Status204NoContent];

        public async Task OnResultExecutionAsync(ResultExecutingContext ctx, ResultExecutionDelegate next)
        {
            switch (ctx.Result)
            {
                case ObjectResult obj:
                    WrapObjectResult(ctx, obj);
                    break;

                case StatusCodeResult sc:
                    WrapStatusCodeResult(ctx, sc);
                    break;

                case EmptyResult:
                    ctx.Result = new ObjectResult(new ApiSuccess<object?>("No content", null))
                    { StatusCode = StatusCodes.Status204NoContent };
                    break;
            }

            await next();
        }

        private static bool IsAlreadyWrapped(object? value)
        {
            if (value == null) return false;
            return value is ProblemDetails || (value.GetType().IsGenericType &&
                   value.GetType().GetGenericTypeDefinition() == typeof(ApiSuccess<>));
        }

        private static void WrapObjectResult(ResultExecutingContext ctx, ObjectResult obj)
        {
            var code = obj.StatusCode ?? StatusCodes.Status200OK;

            if (obj.Value is ValidationProblemDetails vpd)
            {
                ctx.Result = new ObjectResult(ToApiError(vpd, code, ctx.HttpContext))
                { StatusCode = code };
                return;
            }

            if (IsAlreadyWrapped(obj.Value))
                return;

            if (SuccessCodes.Contains(code))
            {
                ctx.Result = new ObjectResult(new ApiSuccess<object?>("OK", obj.Value))
                { StatusCode = code };
                return;
            }

            ctx.Result = new ObjectResult(ToApiError(obj.Value, code, ctx.HttpContext))
            { StatusCode = code };
        }

        private static void WrapStatusCodeResult(ResultExecutingContext ctx, StatusCodeResult sc)
        {
            if (SuccessCodes.Contains(sc.StatusCode))
            {
                ctx.Result = new ObjectResult(new ApiSuccess<object?>("OK", null))
                { StatusCode = sc.StatusCode };
            }
            else
            {
                var msg = ReasonPhrases.GetReasonPhrase(sc.StatusCode);
                ctx.Result = new ObjectResult(ToApiError(msg, sc.StatusCode, ctx.HttpContext))
                { StatusCode = sc.StatusCode };
                //ctx.Result = new ObjectResult(new ApiError(msg, null, ctx.HttpContext.TraceIdentifier))
                //{ StatusCode = sc.StatusCode };
            }
        }

        public static ProblemDetails ToApiError(
            object? value,
            int statusCode,
                HttpContext httpContext)
        {
            ProblemDetails problem = value switch
            {
                ValidationProblemDetails vpd => new ProblemDetails
                {
                    Title = vpd.Title ?? ReasonPhrases.GetReasonPhrase(statusCode),
                    Detail = FormatValidationErrors(vpd.Errors),
                    Status = vpd.Status ?? statusCode,
                    Type = vpd.Type,
                    Instance = vpd.Instance
                },

                ProblemDetails existing => existing,

                string msg => new ProblemDetails
                {
                    Title = ReasonPhrases.GetReasonPhrase(statusCode),
                    Detail = msg,
                    Status = statusCode
                },

                _ => new ProblemDetails
                {
                    Title = ReasonPhrases.GetReasonPhrase(statusCode),
                    Detail = value?.ToString(),
                    Status = statusCode
                }
            };

            problem.Status ??= statusCode;
            problem.Type ??= $"https://httpstatuses.com/{problem.Status}";
            problem.Instance ??= httpContext.Request.Path;

            if (!problem.Extensions.ContainsKey("traceId"))
                problem.Extensions["traceId"] = httpContext.TraceIdentifier;

            return problem;
        }

        private static string FormatValidationErrors(IDictionary<string, string[]> errors)
        {
            var errorMessages = errors.SelectMany(e =>
                e.Value.Select(msg => $"{e.Key}: {msg}"));
            return string.Join("; ", errorMessages);
        }

    }
}