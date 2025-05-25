using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;
using static Toko.Controllers.RoomController;

public class ApiWrapperFilter : IAsyncResultFilter
{
    private static readonly HashSet<int> SuccessCodes =
        new() { StatusCodes.Status200OK, StatusCodes.Status201Created, StatusCodes.Status204NoContent };

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

                    if (SuccessCodes.Contains(code))
                        ctx.Result = new ObjectResult(
                            new ApiSuccess<object?>("OK", obj.Value))
                        { StatusCode = code };
                    else
                        ctx.Result = new ObjectResult(
                            new ApiError(obj.Value?.ToString() ?? ReasonPhrases.GetReasonPhrase(code)))
                        { StatusCode = code };
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
}
