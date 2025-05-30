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
            //await next(); 

            //if (ctx.HttpContext.Response.HasStarted) 
            //    return;

            switch (ctx.Result)
            {
                case ObjectResult obj:
                    {
                        var code = obj.StatusCode ?? StatusCodes.Status200OK;

                        if (IsAlreadyWrapped(obj.Value))
                            break;

                        if (SuccessCodes.Contains(code))
                            ctx.Result = new ObjectResult(
                                new ApiSuccess<object?>("OK", obj.Value))
                            { StatusCode = code };
                        else
                        {
                            // If the value is ProblemDetails / ValidationProblemDetails, preserve it.
                            object errorPayload = obj.Value is ProblemDetails
                                                    ? obj.Value
                                                    : obj.Value?.ToString()
                                                       ?? ReasonPhrases.GetReasonPhrase(code);

                            ctx.Result = new ObjectResult(new ApiError(errorPayload))
                            { StatusCode = code };
                        }
                        break;
                    }

                case StatusCodeResult sc:
                    {
                        if (SuccessCodes.Contains(sc.StatusCode))
                            ctx.Result = new ObjectResult(new ApiSuccess<object?>("OK", null))
                            { StatusCode = sc.StatusCode };
                        else
                            ctx.Result = new ObjectResult(
                                new ApiError(ReasonPhrases.GetReasonPhrase(sc.StatusCode)))
                            { StatusCode = sc.StatusCode };
                        break;
                    }

                case EmptyResult:
                    {
                        ctx.Result = new ObjectResult(new ApiSuccess<object?>("No content", null))
                        { StatusCode = StatusCodes.Status204NoContent };
                        break;
                    }
            }
            await next();
        }

        private bool IsAlreadyWrapped(object? value)
        {
            if (value == null) return false;
            var type = value.GetType();
            if (type == typeof(ApiError)) return true;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ApiSuccess<>)) return true;
            return false;
        }
    }
}