using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamLocum.Web.Domain;
using TeamLocum.Web.Services;
using TeamLocum.Web.ViewModels;

namespace TeamLocum.Web.Controllers;

[Authorize]
public class ClientsController(DirectoryService directory, IActorAccessor actors) : Controller
{
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Index(ClientStatus? status, CancellationToken ct)
    {
        var actor = await actors.RequireAsync(ct);
        return View(new ClientIndexViewModel(await directory.ListClientsAsync(actor, status, ct), status));
    }

    [HttpGet]
    [Authorize(Roles = Roles.Client)]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        var actor = await actors.RequireAsync(ct);
        return View(new ClientProfileViewModel(actor, await directory.GetMyClientProfileAsync(actor, ct)));
    }

    [HttpPost]
    [Authorize(Roles = Roles.Client)]
    public async Task<IActionResult> Profile([Bind(Prefix = "Input")] ClientProfileInput input, CancellationToken ct)
    {
        var actor = await actors.RequireAsync(ct);
        if (!ModelState.IsValid)
        {
            return View(new ClientProfileViewModel(actor, input));
        }

        await directory.UpdateMyClientProfileAsync(actor, input, ct);
        TempData["Success"] = "Profile saved.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public Task<IActionResult> Approve(int id, CancellationToken ct) => Change(id, ClientStatus.Accepted, "Hospital approved.", ct);

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public Task<IActionResult> Reject(int id, CancellationToken ct) => Change(id, ClientStatus.Rejected, "Hospital rejected.", ct);

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public Task<IActionResult> Suspend(int id, CancellationToken ct) => Change(id, ClientStatus.Suspended, "Hospital suspended.", ct);

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public Task<IActionResult> Reinstate(int id, CancellationToken ct) => Change(id, ClientStatus.Accepted, "Hospital reinstated.", ct);

    private async Task<IActionResult> Change(int id, ClientStatus next, string success, CancellationToken ct)
    {
        var actor = await actors.RequireAsync(ct);
        await directory.ChangeClientStatusAsync(actor, id, next, ct);
        TempData["Success"] = success;
        return RedirectToAction(nameof(Index));
    }
}
