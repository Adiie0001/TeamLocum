using Microsoft.AspNetCore.Identity;
using TeamLocum.Web.Data;
using TeamLocum.Web.Models;

namespace TeamLocum.Web.Data
{
    public static class SeedData
    {
        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            // Ensure DB exists
            context.Database.EnsureCreated();

            // Seed Roles
            string[] roles = { "Admin", "Client", "Locum" };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }

            // Seed Admin User
            if (await userManager.FindByEmailAsync("admin@teamlocum.com") == null)
            {
                var admin = new ApplicationUser
                {
                    UserName = "admin@teamlocum.com",
                    Email = "admin@teamlocum.com",
                    FirstName = "System",
                    LastName = "Admin",
                    UserType = "Admin",
                    EmailConfirmed = true
                };
                var result = await userManager.CreateAsync(admin, "Admin@123");
                if (result.Succeeded)
                    await userManager.AddToRoleAsync(admin, "Admin");
            }

            // Seed Hospital Client Users
            var hospitals = new[]
            {
                new { Email = "city.hospital@teamlocum.com", Name = "City General Hospital", First = "Sarah", Last = "Thompson" },
                new { Email = "queens.medical@teamlocum.com", Name = "Queen's Medical Centre", First = "James", Last = "Patel" },
                new { Email = "north.clinic@teamlocum.com", Name = "North Community Clinic", First = "Emma", Last = "Williams" }
            };

            foreach (var h in hospitals)
            {
                if (await userManager.FindByEmailAsync(h.Email) == null)
                {
                    var user = new ApplicationUser
                    {
                        UserName = h.Email, Email = h.Email,
                        FirstName = h.First, LastName = h.Last,
                        UserType = "Client", EmailConfirmed = true
                    };
                    var result = await userManager.CreateAsync(user, "Client@123");
                    if (result.Succeeded)
                    {
                        await userManager.AddToRoleAsync(user, "Client");
                        context.Clients.Add(new Client
                        {
                            UserId = user.Id,
                            CompanyName = h.Name,
                            Status = "Accepted",
                            IsAccredited = true
                        });
                    }
                }
            }

            // Seed Locum Doctor Users
            var doctors = new[]
            {
                new { Email = "dr.sharma@teamlocum.com", First = "Rajesh", Last = "Sharma", Gmc = "7654321" },
                new { Email = "dr.jones@teamlocum.com", First = "Emily", Last = "Jones", Gmc = "8901234" },
                new { Email = "dr.khan@teamlocum.com", First = "Omar", Last = "Khan", Gmc = "5678901" },
                new { Email = "dr.patel@teamlocum.com", First = "Priya", Last = "Patel", Gmc = "2345678" },
                new { Email = "dr.chen@teamlocum.com", First = "Wei", Last = "Chen", Gmc = "9012345" }
            };

            foreach (var d in doctors)
            {
                if (await userManager.FindByEmailAsync(d.Email) == null)
                {
                    var user = new ApplicationUser
                    {
                        UserName = d.Email, Email = d.Email,
                        FirstName = d.First, LastName = d.Last,
                        UserType = "Locum", EmailConfirmed = true
                    };
                    var result = await userManager.CreateAsync(user, "Locum@123");
                    if (result.Succeeded)
                    {
                        await userManager.AddToRoleAsync(user, "Locum");
                        context.Locums.Add(new Locum
                        {
                            UserId = user.Id,
                            GmcNumber = d.Gmc,
                            Status = "Active"
                        });
                    }
                }
            }

            await context.SaveChangesAsync();

            // Seed Sample Bookings
            if (!context.Bookings.Any())
            {
                var client = context.Clients.FirstOrDefault();
                var locum = context.Locums.FirstOrDefault();
                if (client != null && locum != null)
                {
                    context.Bookings.AddRange(
                        new Booking
                        {
                            ClientId = client.Id, LocumId = locum.Id,
                            Date = DateTime.Today.AddDays(1),
                            StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(17, 0, 0),
                            RatePerHour = 85.00m, Location = "City General Hospital, Ward 3",
                            Status = "Filled", Notes = "Paediatrics cover"
                        },
                        new Booking
                        {
                            ClientId = client.Id, LocumId = null,
                            Date = DateTime.Today.AddDays(3),
                            StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(16, 0, 0),
                            RatePerHour = 90.00m, Location = "City General Hospital, A&E",
                            Status = "Open", Notes = "Emergency department cover needed"
                        },
                        new Booking
                        {
                            ClientId = client.Id, LocumId = null,
                            Date = DateTime.Today.AddDays(5),
                            StartTime = new TimeSpan(18, 0, 0), EndTime = new TimeSpan(22, 0, 0),
                            RatePerHour = 110.00m, Location = "North Community Clinic",
                            Status = "Open", Notes = "Evening out-of-hours GP session"
                        }
                    );
                    await context.SaveChangesAsync();
                }
            }

            // Seed UK Bank Holidays
            if (!context.Holidays.Any())
            {
                context.Holidays.AddRange(
                    new Holiday { Date = new DateTime(2025, 12, 25), Details = "Christmas Day" },
                    new Holiday { Date = new DateTime(2025, 12, 26), Details = "Boxing Day" },
                    new Holiday { Date = new DateTime(2026, 1, 1), Details = "New Year's Day" },
                    new Holiday { Date = new DateTime(2026, 4, 3), Details = "Good Friday" },
                    new Holiday { Date = new DateTime(2026, 4, 6), Details = "Easter Monday" },
                    new Holiday { Date = new DateTime(2026, 5, 4), Details = "Early May Bank Holiday" },
                    new Holiday { Date = new DateTime(2026, 5, 25), Details = "Spring Bank Holiday" },
                    new Holiday { Date = new DateTime(2026, 8, 31), Details = "Summer Bank Holiday" }
                );
                await context.SaveChangesAsync();
            }
        }
    }
}
