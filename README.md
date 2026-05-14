# 🏥 TeamLocum — Healthcare Staffing Management System

[![.NET Build & Test](https://github.com/Adiie0001/TeamLocum/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Adiie0001/TeamLocum/actions/workflows/dotnet.yml)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core_MVC-9.0-512BD4?style=flat-square&logo=.net&logoColor=white)
![EF Core](https://img.shields.io/badge/Entity_Framework_Core-9.0-512BD4?style=flat-square&logo=.net&logoColor=white)
![SQL Server](https://img.shields.io/badge/SQL_Server-LocalDB-CC2927?style=flat-square&logo=microsoft-sql-server&logoColor=white)
![Identity](https://img.shields.io/badge/ASP.NET_Identity-RBAC-green?style=flat-square)

A production-grade **Healthcare Staffing Web Application** built with **ASP.NET Core MVC 9** that connects locum doctors with hospitals and clinics. Developed as MCA Capstone Project — designed to solve real NHS locum staffing challenges.

---

## 🚀 Features

- ✅ **ASP.NET Identity** — Role-based authentication (Admin / Client / Locum)
- ✅ **Automated Job Matching** — Smart time-overlap detection prevents double-booking
- ✅ **Booking Management** — Create, fill, and track healthcare staffing bookings
- ✅ **Locum Management** — GMC number verification, approve/reject workflow
- ✅ **Client Management** — Hospital onboarding with CARAS accreditation tracking
- ✅ **Bank Holiday Awareness** — UK bank holidays tracked to avoid scheduling conflicts
- ✅ **Auto Seed Data** — Demo users, hospitals, doctors, bookings on first run
- ✅ **CI/CD** — GitHub Actions build & test pipeline

---

## 🛠️ Tech Stack

| Layer | Technology |
|-------|-----------|
| Framework | ASP.NET Core MVC 9 |
| Auth | ASP.NET Identity + RBAC |
| ORM | Entity Framework Core 9 |
| Database | SQL Server / LocalDB |
| Frontend | Razor Views + Bootstrap 5 |
| CI/CD | GitHub Actions |

---

## ⚡ Quick Start

### Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- SQL Server LocalDB (included with Visual Studio)

### Run Locally

```bash
git clone https://github.com/Adiie0001/TeamLocum.git
cd TeamLocum
dotnet restore
dotnet run --project TeamLocum.Web
```

Open browser: **http://localhost:5xxx**

> 💡 Database is auto-created and seeded on first run — no manual migrations needed!

---

## 🔑 Demo Credentials

| Role | Email | Password |
|------|-------|----------|
| **Admin** | admin@teamlocum.com | Admin@123 |
| **Hospital** | city.hospital@teamlocum.com | Client@123 |
| **Doctor** | dr.sharma@teamlocum.com | Locum@123 |

---

## 🏗️ Project Structure

```
TeamLocum/
├── TeamLocum.sln
└── TeamLocum.Web/
    ├── Controllers/
    │   ├── BookingsController.cs   # [Authorize] — booking CRUD + job matching
    │   ├── ClientsController.cs    # [Authorize] — hospital management
    │   ├── LocumsController.cs     # [Authorize] — doctor approve/reject
    │   ├── HolidaysController.cs   # [Authorize] — UK bank holiday management
    │   └── HomeController.cs       # Public — landing page
    ├── Data/
    │   ├── ApplicationDbContext.cs # IdentityDbContext with all entities
    │   └── SeedData.cs             # Demo: Admin + 3 hospitals + 5 doctors + bookings
    ├── Models/
    │   └── Entities.cs             # ApplicationUser, Client, Locum, Booking, Holiday
    ├── Views/                      # Razor views per controller
    └── Program.cs                  # App configuration with Identity + Roles
```

---

## 🧠 Core Algorithm — Automated Job Matching

The booking system automatically finds available locums for any time slot by detecting **time overlaps**:

```csharp
// Conflict detection: (NewStart < ExistingEnd) AND (NewEnd > ExistingStart)
var availableLocums = allLocums.Where(locum => {
    var locumBookings = existingBookings.Where(b => b.LocumId == locum.Id);
    foreach (var booking in locumBookings) {
        if (start < booking.EndTime && end > booking.StartTime)
            return false; // Conflict!
    }
    return true; // Available
});
```

---

## 👥 User Roles & Permissions

| Action | Admin | Client | Locum |
|--------|-------|--------|-------|
| View all bookings | ✅ | ✅ | ✅ |
| Create booking | ✅ | ✅ | ❌ |
| Approve locum | ✅ | ❌ | ❌ |
| Manage holidays | ✅ | ❌ | ❌ |
| View all clients | ✅ | ❌ | ❌ |

---

## 🔮 Roadmap

- [ ] Doctor self-registration with GMC verification
- [ ] Real-time booking notifications (SignalR)
- [ ] Invoice generation (PDF)
- [ ] Mobile-responsive dashboard upgrade
- [ ] Docker Compose support
- [ ] Azure deployment

---

## 📄 Background

Built as MCA Capstone Project at **VNSGU, Surat**. The application models the real UK NHS locum staffing workflow — hospitals post shifts, doctors apply, administrators manage the matching process — all with proper role-based access control.

---

## 👨‍💻 Author

**Aditya Maisuriya** — ASP.NET Core Developer
- 🌐 Portfolio: [adityamaisuriya.pages.dev](https://adityamaisuriya.pages.dev)
- 💼 LinkedIn: [linkedin.com/in/aditya-maisuriya-39a540202](https://linkedin.com/in/aditya-maisuriya-39a540202)
- 📧 Email: adiiimaisuriya94@gmail.com
