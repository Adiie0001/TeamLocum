using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TeamLocum.Web.Data;
using TeamLocum.Web.Models;

namespace TeamLocum.Web.Controllers
{
    [Authorize]
    public class BookingsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public BookingsController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var bookings = _context.Bookings.Include(b => b.Client).Include(b => b.Locum);
            return View(await bookings.ToListAsync());
        }

        public IActionResult Create()
        {
            ViewData["ClientId"] = new SelectList(_context.Clients, "Id", "CompanyName");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,ClientId,LocumId,Date,StartTime,EndTime,RatePerHour,Location,Notes")] Booking booking)
        {
            if (ModelState.IsValid)
            {
                // Set status based on locum assignment
                booking.Status = booking.LocumId.HasValue ? "Filled" : "Open";
                _context.Add(booking);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["ClientId"] = new SelectList(_context.Clients, "Id", "CompanyName", booking.ClientId);
            return View(booking);
        }

        // Automated Job Matching: Get available locums for a specific time slot
        [HttpGet]
        public async Task<IActionResult> GetAvailableLocums(DateTime date, string startTime, string endTime)
        {
            if (!TimeSpan.TryParse(startTime, out var start) || !TimeSpan.TryParse(endTime, out var end))
            {
                return BadRequest("Invalid time format");
            }

            // Get all active locums
            var allLocums = await _context.Locums
                .Include(l => l.User)
                .Where(l => l.Status == "Active" || l.Status == "Accepted")
                .ToListAsync();

            // Get all bookings for the specified date
            var existingBookings = await _context.Bookings
                .Where(b => b.Date.Date == date.Date && b.LocumId.HasValue)
                .ToListAsync();

            // Filter out locums with conflicting bookings
            var availableLocums = allLocums.Where(locum =>
            {
                var locumBookings = existingBookings.Where(b => b.LocumId == locum.Id);
                
                // Check for time overlap: (NewStart < ExistingEnd) AND (NewEnd > ExistingStart)
                foreach (var booking in locumBookings)
                {
                    if (start < booking.EndTime && end > booking.StartTime)
                    {
                        return false; // Conflict found
                    }
                }
                return true; // No conflicts
            }).Select(l => new
            {
                id = l.Id,
                name = $"{l.User.FirstName} {l.User.LastName}",
                gmcNumber = l.GmcNumber ?? "N/A"
            }).ToList();

            return Json(availableLocums);
        }
    
public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var booking = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Locum)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (booking == null)
            {
                return NotFound();
            }

            return View(booking);
        }

        // GET: Bookings/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var booking = await _context.Bookings.FindAsync(id);
            if (booking == null)
            {
                return NotFound();
            }
            ViewData["ClientId"] = new SelectList(_context.Clients, "Id", "CompanyName", booking.ClientId);
            ViewData["LocumId"] = new SelectList(_context.Locums, "Id", "GmcNumber", booking.LocumId);
            return View(booking);
        }

        // POST: Bookings/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,ClientId,LocumId,Date,StartTime,EndTime,RatePerHour,Location,Status,Notes")] Booking booking)
        {
            if (id != booking.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(booking);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!BookingExists(booking.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["ClientId"] = new SelectList(_context.Clients, "Id", "CompanyName", booking.ClientId);
            ViewData["LocumId"] = new SelectList(_context.Locums, "Id", "GmcNumber", booking.LocumId);
            return View(booking);
        }

        // GET: Bookings/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var booking = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Locum)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (booking == null)
            {
                return NotFound();
            }

            return View(booking);
        }

        // POST: Bookings/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var booking = await _context.Bookings.FindAsync(id);
            if (booking != null)
            {
                _context.Bookings.Remove(booking);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool BookingExists(int id)
        {
            return _context.Bookings.Any(e => e.Id == id);
        }
    
    }
}


