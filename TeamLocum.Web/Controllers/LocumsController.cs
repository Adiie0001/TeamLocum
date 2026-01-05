using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeamLocum.Web.Data;
using TeamLocum.Web.Models;

namespace TeamLocum.Web.Controllers
{
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
    }
}
