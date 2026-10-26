using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using TeamLocum.Web.Domain;

namespace TeamLocum.Web.Infrastructure;

/// <summary>
/// Turns domain failures into the right browser experience. A record that does not exist (or that the caller may not
/// know exists) is a 404, a missing permission is a redirect to "access denied", and a rule that was broken (an
/// overlapping shift, an illegal status change) is explained to the user on the page they came from.
/// </summary>
public sealed class DomainExceptionFilter(ITempDataDictionaryFactory tempData, ILogger<DomainExceptionFilter> logger) : IExceptionFilter
{
    public const string ErrorKey = "Error";

    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not DomainException exception)
        {
            return;
        }

        context.ExceptionHandled = true;
        logger.LogInformation("Request rejected: {Type}: {Message}", exception.GetType().Name, exception.Message);

        switch (exception)
        {
            case NotFoundException:
                context.Result = new NotFoundResult();
                return;
            case ForbiddenException:
                context.Result = new ForbidResult();
                return;
        }

        var http = context.HttpContext;
        if (HttpMethods.IsPost(http.Request.Method))
        {
            // Saved explicitly: a result produced by an exception filter does not pass through the usual TempData save step.
            var flash = tempData.GetTempData(http);
            flash[ErrorKey] = exception.Message;
            flash.Save();
            context.Result = new LocalRedirectResult(ReturnTarget(http));
            return;
        }

        context.Result = new ViewResult
        {
            ViewName = "~/Views/Shared/Problem.cshtml",
            ViewData = new ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),
                context.ModelState) { Model = exception.Message },
            StatusCode = exception is ConflictException ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest
        };
    }

    /// <summary>Back to the page the form was on, but only ever to this site.</summary>
    internal static string ReturnTarget(HttpContext http)
    {
        var referrer = http.Request.Headers.Referer.ToString();
        if (Uri.TryCreate(referrer, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Authority, http.Request.Host.Value, StringComparison.OrdinalIgnoreCase))
        {
            return uri.PathAndQuery;
        }

        return "/";
    }
}
