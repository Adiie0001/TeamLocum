using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using TeamLocum.Web.Domain;
using TeamLocum.Web.Services;
using TeamLocum.Web.ViewModels;

namespace TeamLocum.Web.Controllers;

[Authorize]
public class BookingsController(BookingService bookings, DirectoryService directory, IActorAccessor actors) : Controller
{
    private const string Posters = Roles.Admin + "," + Roles.Client;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] BookingListQuery query, CancellationToken ct)
    {
        var actor = await actors.RequireAsync(ct);
        var result = await bookings.ListAsync(actor, query, ct);
        return View(new BookingIndexViewModel(actor, query, result));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var actor = await actors.RequireAsync(ct);
        var details = await bookings.GetAsync(actor, id, ct);
        var matches = details.Permissions.CanAssign ? await bookings.MatchesAsync(actor, id, ct) : null;
        return View(new BookingDetailsViewModel(actor, details, matches));
    }

    [HttpGet]
    [Authorize(Roles = Posters)]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var actor = await actors.RequireAsync(ct);
        var start = DateTime.Today.AddDays(7).AddHours(9);
        var input = new BookingInput { StartsAt = start, EndsAt = start.AddHours(8), RatePerHour = 80m };
        return View("Form", await FormModelAsync(actor, input, isEdit: false, ct));
    }

    [HttpPost]
    [Authorize(Roles = Posters)]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] BookingInput input, CancellationToken ct)
    {
        var actor = await actors.RequireAsync(ct);
        if (ModelState.IsValid)
        {
            try
            {
                var saved = await bookings.CreateAsync(actor, input, ct);
                TempData["Success"] = "Shift posted. Doctors can now apply.";
                return RedirectToAction(nameof(Details), new { id = saved.Id });
            }
            catch (DomainException ex) when (ex is not (NotFoundException or ForbiddenException))
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }

        return View("Form", await FormModelAsync(actor, input, isEdit: false, ct));
    }

    [HttpGet]
    [Authorize(Roles = Posters)]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var actor = await actors.RequireAsync(ct);
        var input = await bookings.GetForEditAsync(actor, id, ct);
        return View("Form", await FormModelAsync(actor, input, isEdit: true, ct, id));
    }

    [HttpPost]
    [Authorize(Roles = Posters)]
    public async Task<IActionResult> Edit(int id, [Bind(Prefix = "Input")] BookingInput input, CancellationToken ct)
    {
        var actor = await actors.RequireAsync(ct);
        if (ModelState.IsValid)
        {
            try
            {
                await bookings.UpdateAsync(actor, id, input, ct);
                TempData["Success"] = "Shift updated.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (DomainException ex) when (ex is not (NotFoundException or ForbiddenException))
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }

        return View("Form", await FormModelAsync(actor, input, isEdit: true, ct, id));
    }

    [HttpPost]
    public Task<IActionResult> Cancel(int id, CancellationToken ct) =>
        Act(id, "Shift cancelled.", (actor, token) => bookings.CancelAsync(actor, id, token), ct);

    [HttpPost]
    public Task<IActionResult> Complete(int id, CancellationToken ct) =>
        Act(id, "Shift marked as completed.", (actor, token) => bookings.CompleteAsync(actor, id, token), ct);

    [HttpPost]
    public Task<IActionResult> Release(int id, CancellationToken ct) =>
        Act(id, "The doctor was released and the shift is open again.", (actor, token) => bookings.ReleaseAsync(actor, id, token), ct);

    [HttpPost]
    public Task<IActionResult> Assign(int id, int locumId, Guid stamp, CancellationToken ct) =>
        Act(id, "Doctor assigned. The shift is filled.", (actor, token) => bookings.AssignAsync(actor, id, locumId, stamp, token), ct);

    [HttpPost]
    public Task<IActionResult> Apply(int id, string? message, CancellationToken ct) =>
        Act(id, "Application sent. The hospital will review it.", (actor, token) => bookings.ApplyAsync(actor, id, message, token), ct);

    [HttpPost]
    public Task<IActionResult> Withdraw(int id, CancellationToken ct) =>
        Act(id, "Application withdrawn.", (actor, token) => bookings.WithdrawAsync(actor, id, token), ct);

    private async Task<IActionResult> Act(int id, string success, Func<Actor, CancellationToken, Task> action, CancellationToken ct)
    {
        var actor = await actors.RequireAsync(ct);
        await action(actor, ct);
        TempData["Success"] = success;
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<BookingFormViewModel> FormModelAsync(
        Actor actor, BookingInput input, bool isEdit, CancellationToken ct, int? id = null)
    {
        IReadOnlyList<SelectListItem> clients = [];
        if (actor.IsAdmin && !isEdit)
        {
            clients = (await directory.ListClientsAsync(actor, ClientStatus.Accepted, ct))
                .Select(c => new SelectListItem(c.CompanyName, c.Id.ToString(), c.Id == input.ClientId))
                .ToList();
        }

        return new BookingFormViewModel(input, isEdit, id, clients);
    }
}
