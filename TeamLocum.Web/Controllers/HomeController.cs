using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TeamLocum.Web.Infrastructure.Middleware;
using TeamLocum.Web.Services;
using TeamLocum.Web.ViewModels;

namespace TeamLocum.Web.Controllers;

public class HomeController(IActorAccessor actors, DirectoryService directory) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var actor = await actors.GetAsync(ct);
        var items = actor is null ? [] : await directory.DashboardAsync(actor, ct);
        return View(new HomeViewModel(actor, items));
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(ErrorModel(StatusCodes.Status500InternalServerError));

    /// <summary>Friendly pages for 4xx responses produced anywhere in the pipeline.</summary>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult HttpStatus(int code)
    {
        // The status comes from the query string when this page is opened directly; only real error statuses are honoured.
        var status = code is >= 400 and <= 599 ? code : StatusCodes.Status404NotFound;
        Response.StatusCode = status;
        return View("Error", ErrorModel(status));
    }

    private ErrorViewModel ErrorModel(int status) => new(
        Activity.Current?.Id ?? HttpContext.TraceIdentifier,
        HttpContext.Items[CorrelationIdMiddleware.ItemKey] as string,
        status);
}
