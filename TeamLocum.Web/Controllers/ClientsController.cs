using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeamLocum.Web.Data;
using TeamLocum.Web.Models;

namespace TeamLocum.Web.Controllers
{
    public class ClientsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ClientsController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            return View(await _context.Clients.Include(c => c.User).ToListAsync());
        }

        // GET: Clients/Masquerade/5
        public IActionResult Masquerade(int? id)
        {
            if (id == null) return NotFound();
            // In a real app, strict security checks here!
            // For showcase, we just "pretend" by redirecting to a client dashboard with their context
            return RedirectToAction("Index", "Dashboard", new { area = "Client", clientId = id });
        }

        public async Task<IActionResult> Approve(int id)
        {
             var client = await _context.Clients.FindAsync(id);
             if (client != null) {
                 client.Status = "Accepted";
                 await _context.SaveChangesAsync();
             }
             return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Reject(int id)
        {
             var client = await _context.Clients.FindAsync(id);
             if (client != null) {
                 client.Status = "Rejected";
                 await _context.SaveChangesAsync();
             }
             return RedirectToAction(nameof(Index));
        }
    }
}
