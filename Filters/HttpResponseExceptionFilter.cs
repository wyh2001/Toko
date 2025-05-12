using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using static Toko.HttpExceptions.HttpExceptions;

namespace Toko.Filters
{
    public class HttpResponseExceptionFilter : IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            switch (context.Exception)
            {
                case NotFoundException notFound:
                    context.Result = new NotFoundObjectResult(new { error = notFound.Message });
                    break;

                case BadRequestException badReq:
                    context.Result = new BadRequestObjectResult(new { error = badReq.Message });
                    break;

                default:
                    // 未捕获的异常，返回 500
                    context.Result = new ObjectResult(new { error = "Internal Server Error" })
                    {
                        StatusCode = StatusCodes.Status500InternalServerError
                    };
                    break;
            }

            context.ExceptionHandled = true;
        }
    }
}
