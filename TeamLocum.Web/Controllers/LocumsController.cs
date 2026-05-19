using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TeamLocum.Web.Data;
using TeamLocum.Web.Models;

namespace TeamLocum.Web.Controllers
{
    [Authorize]
    public class LocumsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public LocumsController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            return View(await _context.Locums.Include(l => l.User).ToListAsync());
        }

        public async Task<IActionResult> Approve(int id)
        {
             var locum = await _context.Locums.FindAsync(id);
             if (locum != null) {
                 locum.Status = "Accepted";
                 await _context.SaveChangesAsync();
             }
             return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Reject(int id)
        {
             var locum = await _context.Locums.FindAsync(id);
             if (locum != null) {
                 locum.Status = "Rejected";
                 await _context.SaveChangesAsync();
             }
             return RedirectToAction(nameof(Index));
        }
    
public IActionResult Create()
        {
            ViewData["UserId"] = new SelectList(_context.Users, "Id", "Id");
            return View();
        }

        // POST: Locums/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,UserId,CarasId,Status,ResumePath,GmcNumber")] Locum locum)
        {
            if (ModelState.IsValid)
            {
                _context.Add(locum);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["UserId"] = new SelectList(_context.Users, "Id", "Email", locum.UserId);
            return View(locum);
        }

        // GET: Locums/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var locum = await _context.Locums.FindAsync(id);
            if (locum == null)
            {
                return NotFound();
            }
            ViewData["UserId"] = new SelectList(_context.Users, "Id", "Email", locum.UserId);
            return View(locum);
        }

        // POST: Locums/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,UserId,CarasId,Status,ResumePath,GmcNumber")] Locum locum)
        {
            if (id != locum.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(locum);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!LocumExists(locum.Id))
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
            ViewData["UserId"] = new SelectList(_context.Users, "Id", "Email", locum.UserId);
            return View(locum);
        }

        // GET: Locums/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var locum = await _context.Locums
                .Include(l => l.User)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (locum == null)
            {
                return NotFound();
            }

            return View(locum);
        }

        // POST: Locums/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var locum = await _context.Locums.FindAsync(id);
            if (locum != null)
            {
                _context.Locums.Remove(locum);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool LocumExists(int id)
        {
            return _context.Locums.Any(e => e.Id == id);
        }
    
    }
}


