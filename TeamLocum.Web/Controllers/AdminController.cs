using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeamLocum.Web.Data;
using TeamLocum.Web.Models;

namespace TeamLocum.Web.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public AdminController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var totalUsers = await _userManager.Users.CountAsync();
            var totalBookings = await _context.Bookings.CountAsync();
            var openBookings = await _context.Bookings.CountAsync(b => b.Status == "Open");
            var totalHospitals = await _context.Clients.CountAsync();
            var totalLocums = await _context.Locums.CountAsync();

            ViewBag.TotalUsers = totalUsers;
            ViewBag.TotalBookings = totalBookings;
            ViewBag.OpenBookings = openBookings;
            ViewBag.TotalHospitals = totalHospitals;
            ViewBag.TotalLocums = totalLocums;

            return View();
        }

        public async Task<IActionResult> Users()
        {
            var users = await _userManager.Users.ToListAsync();
            return View(users);
        }
    }
}
