using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamLocum.Web.Domain;
using TeamLocum.Web.Services;
using TeamLocum.Web.ViewModels;

namespace TeamLocum.Web.Controllers;

[Authorize]
public class LocumsController(DirectoryService directory, IActorAccessor actors) : Controller
{
    [HttpGet]
    [Authorize(Roles = Roles.Admin + "," + Roles.Client)]
    public async Task<IActionResult> Index(string? search, LocumStatus? status, CancellationToken ct)
    {
        var actor = await actors.RequireAsync(ct);
        var rows = await directory.ListLocumsAsync(actor, search, status, ct);
        return View(new LocumIndexViewModel(actor, rows, search, status));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var actor = await actors.RequireAsync(ct);
        return View(new LocumDetailsViewModel(actor, await directory.GetLocumAsync(actor, id, ct)));
    }

    [HttpGet]
    [Authorize(Roles = Roles.Locum)]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        var actor = await actors.RequireAsync(ct);
        return View(new LocumProfileViewModel(actor, await directory.GetMyLocumProfileAsync(actor, ct)));
    }

    [HttpPost]
    [Authorize(Roles = Roles.Locum)]
    public async Task<IActionResult> Profile([Bind(Prefix = "Input")] LocumProfileInput input, CancellationToken ct)
    {
        var actor = await actors.RequireAsync(ct);
        if (!ModelState.IsValid)
        {
            return View(new LocumProfileViewModel(actor, input));
        }

        await directory.UpdateMyLocumProfileAsync(actor, input, ct);
        TempData["Success"] = "Profile saved.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public Task<IActionResult> Approve(int id, CancellationToken ct) => Change(id, LocumStatus.Active, "Doctor approved.", ct);

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public Task<IActionResult> Reject(int id, CancellationToken ct) => Change(id, LocumStatus.Rejected, "Doctor rejected.", ct);

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public Task<IActionResult> Suspend(int id, CancellationToken ct) => Change(id, LocumStatus.Suspended, "Doctor suspended.", ct);

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public Task<IActionResult> Reinstate(int id, CancellationToken ct) => Change(id, LocumStatus.Active, "Doctor reinstated.", ct);

    private async Task<IActionResult> Change(int id, LocumStatus next, string success, CancellationToken ct)
    {
        var actor = await actors.RequireAsync(ct);
        await directory.ChangeLocumStatusAsync(actor, id, next, ct);
        TempData["Success"] = success;
        return RedirectToAction(nameof(Index));
    }
}
