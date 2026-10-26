using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeamLocum.Web.Data;
using TeamLocum.Web.Domain;
using TeamLocum.Web.ViewModels;

namespace TeamLocum.Web.Controllers;

[Authorize]
public class HolidaysController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await db.Holidays.AsNoTracking().OrderBy(h => h.Date).ToListAsync(ct));

    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    public IActionResult Create() => View(new HolidayInput { Date = DateOnly.FromDateTime(DateTime.Today) });

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Create(HolidayInput input, CancellationToken ct)
    {
        if (ModelState.IsValid && await db.Holidays.AnyAsync(h => h.Date == input.Date, ct))
        {
            ModelState.AddModelError(nameof(input.Date), "That date is already on the list.");
        }

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        db.Holidays.Add(new Holiday { Date = input.Date, Details = input.Details.Trim() });
        await db.SaveChangesAsync(ct);
        TempData["Success"] = "Bank holiday added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var removed = await db.Holidays.Where(h => h.Id == id).ExecuteDeleteAsync(ct);
        if (removed == 0)
        {
            return NotFound();
        }

        TempData["Success"] = "Bank holiday removed.";
        return RedirectToAction(nameof(Index));
    }
}
