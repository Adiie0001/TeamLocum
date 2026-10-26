using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamLocum.Web.Domain;
using TeamLocum.Web.Services;

namespace TeamLocum.Web.Controllers;

[Authorize(Roles = Roles.Admin)]
public class AdminController(DirectoryService directory, IActorAccessor actors) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var actor = await actors.RequireAsync(ct);
        return View(await directory.DashboardAsync(actor, ct));
    }

    [HttpGet]
    public async Task<IActionResult> Users(CancellationToken ct)
    {
        var actor = await actors.RequireAsync(ct);
        return View(await directory.ListUsersAsync(actor, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Unlock(string id, CancellationToken ct)
    {
        var actor = await actors.RequireAsync(ct);
        await directory.UnlockUserAsync(actor, id, ct);
        TempData["Success"] = "Account unlocked.";
        return RedirectToAction(nameof(Users));
    }
}
