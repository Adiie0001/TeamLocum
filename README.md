# 🏥 Team Locum - Healthcare Staffing Management System

![ASP.NET Core](https://img.shields.io/badge/ASP.NET%20Core-8.0-512BD4?style=flat-square&logo=.net)
![C#](https://img.shields.io/badge/C%23-239120?style=flat-square&logo=c-sharp&logoColor=white)
![Entity Framework](https://img.shields.io/badge/Entity%20Framework-512BD4?style=flat-square&logo=.net)
![SQL Server](https://img.shields.io/badge/SQL%20Server-CC2927?style=flat-square&logo=microsoft-sql-server&logoColor=white)
![Bootstrap](https://img.shields.io/badge/Bootstrap-7952B3?style=flat-square&logo=bootstrap&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)

> A comprehensive **Healthcare Staffing Management System** designed to streamline locum doctor bookings for healthcare organizations. Built with **ASP.NET Core MVC**, featuring **automated job matching**, **RBAC**, and **real-time availability tracking**.

---

## 📋 **Overview**

Team Locum is a production-ready web application that bridges the gap between healthcare facilities and locum doctors. The system intelligently matches available medical professionals with shift requirements, preventing double-booking through real-time conflict detection.

**Key Highlights:**
- ✅ **Automated Job Matching** - Smart AI-powered locum suggestion system
- ✅ **Role-Based Access Control** - Secure Admin, Client, and Locum portals
- ✅ **Real-Time Availability** - AJAX-driven conflict detection
- ✅ **Clean Architecture** - MVC pattern with Entity Framework Core
- ✅ **Production Ready** - Comprehensive CRUD operations and workflows

---

## ✨ **Features**

### **For Administrators**
- ✅ **Account Management** - Review and approve/reject registrations
- ✅ **Masquerade Mode** - Impersonate client accounts for support
- ✅ **Holiday Master** - Manage public holidays for rate calculations
- ✅ **System Oversight** - Monitor all bookings and user activities

### **For Clients (Healthcare Organizations)**
- ✅ **Job Posting** - Create booking requests with shift details
- ✅ **Smart Matching** - View only available locums for selected time slots
- ✅ **Booking Tracker** - Manage active and historical assignments
- ✅ **Dashboard** - Real-time staffing overview

### **For Locums (Medical Professionals)**
- ✅ **Profile Management** - Maintain credentials and availability
- ✅ **Job Opportunities** - Apply for suitable booking slots
- ✅ **Schedule Tracking** - View confirmed and pending assignments

---

## 🛠️ **Tech Stack**

| Technology | Purpose |
|------------|---------|
| **ASP.NET Core 8 MVC** | Web Application Framework |
| **C#** | Programming Language |
| **Entity Framework Core 8** | ORM for database operations |
| **SQL Server (LocalDB)** | Development database |
| **ASP.NET Core Identity** | Authentication & Authorization |
| **Bootstrap 5** | Responsive UI framework |
| **jQuery** | AJAX and DOM manipulation |

---

## 📂 **Project Structure**

```
TeamLocum/
├── TeamLocum.Web/
│   ├── Controllers/          # MVC Controllers
│   │   ├── ClientsController.cs      # Client account management
│   │   ├── LocumsController.cs       # Locum account management
│   │   ├── BookingsController.cs     # Booking CRUD + Matching API
│   │   └── HolidaysController.cs     # Holiday management
│   ├── Data/                 # Database Context
│   │   └── ApplicationDbContext.cs   # EF Core DbContext
│   ├── Models/               # Data Models
│   │   └── Entities.cs              # ApplicationUser, Client, Locum, Booking, Holiday
│   ├── Views/                # Razor Views
│   │   ├── Clients/
│   │   ├── Locums/
│   │   ├── Bookings/
│   │   └── Holidays/
│   ├── wwwroot/              # Static Files (CSS, JS, libraries)
│   └── Program.cs            # Application Entry Point
└── TeamLocum.sln             # Solution File
```

---

## 🚀 **Getting Started**

### **Prerequisites**

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [SQL Server](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) (LocalDB or Express Edition)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) or [VS Code](https://code.visualstudio.com/)

### **Installation**

1️⃣ **Clone the repository**
```bash
git clone https://github.com/Adiie0001/TeamLocum.git
cd TeamLocum
```

2️⃣ **Configure Database Connection**

Update the connection string in `TeamLocum.Web/appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=TeamLocumDb;Trusted_Connection=True;MultipleActiveResultSets=true"
  }
}
```

3️⃣ **Restore Dependencies**
```bash
cd TeamLocum.Web
dotnet restore
```

4️⃣ **Apply Database Migrations**
```bash
dotnet ef database update
```

5️⃣ **Run the Application**
```bash
dotnet run
```

6️⃣ **Access the Application**
```
https://localhost:5001
```

---

## 🎯 **Key Features Implementation**

### **Automated Job Matching ⚡**
**Smart locum availability detection** powered by real-time conflict analysis:
- **AJAX-Driven UI**: Dynamic dropdown updates as you select booking date/time
- **Conflict Detection**: Prevents double-booking with interval overlap algorithm
- **Visual Feedback**: Real-time status updates (✅ available / ⚠️ no matches)
- **Intelligent Filtering**: Only shows locums without schedule conflicts

**How It Works:**
1. User selects Date, Start Time, and End Time for a booking
2. JavaScript automatically calls `/Bookings/GetAvailableLocums` API
3. Backend queries database for locums without conflicting bookings
4. Available locums are displayed with their GMC numbers
5. Booking status auto-updates ("Open" → "Filled")

**Conflict Detection Algorithm:**
```csharp
// Time overlap: (NewStart < ExistingEnd) AND (NewEnd > ExistingStart)
if (newStartTime < existingBooking.EndTime && newEndTime > existingBooking.StartTime)
{
    return false; // Conflict found - exclude this locum
}
```

### **Role-Based Access Control (RBAC)**
User roles are managed through the `UserType` field in `ApplicationUser`, enabling differentiated access:
- **Admin**: Full system access, account approval, masquerade capability
- **Client**: Post jobs, manage bookings, select locums
- **Locum**: Apply for jobs, manage availability, track assignments

### **Account Approval Workflow**
New client and locum registrations require admin approval before gaining full system access, ensuring quality control and verification.

### **Comprehensive Booking Management**
- Date and time selection with validation
- Location specification and notes
- Hourly rate calculation
- Assignment tracking with status updates
- Historical records and reporting

---

## 🗄️ **Database Schema**

### **Core Entities**
- **ApplicationUser**: Extended Identity user with UserType, FirstName, LastName
- **Client**: Healthcare organization with CompanyName, CarasId, AccreditationStatus
- **Locum**: Medical professional with GmcNumber, ResumePath, Credentials
- **Booking**: Shift details with Date, StartTime, EndTime, RatePerHour, Location, Status
- **Holiday**: Public holiday master list for rate adjustments

---

## 🔧 **Development**

### **Adding New Migrations**
```bash
dotnet ef migrations add MigrationName
dotnet ef database update
```

### **Building for Production**
```bash
dotnet publish -c Release -o ./publish
```

---

## 📚 **Future Enhancements**

- [ ] Real-time notifications (Email/SMS)
- [ ] Mobile responsive design improvements
- [ ] Integration with external credentialing systems (CARAS)
- [ ] Payment processing integration
- [ ] Reporting and analytics dashboard
- [ ] API development for third-party integrations

---

## 🤝 **Contributing**

This is a portfolio project. Feel free to fork and adapt for your own use.

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

---

## 📄 **License**

This project is licensed under the **MIT License** - feel free to use it for learning and commercial purposes.

---

## 📞 **Connect With Me**

[![LinkedIn](https://img.shields.io/badge/LinkedIn-0077B5?style=flat-square&logo=linkedin&logoColor=white)](https://linkedin.com/in/aditya-maisuriya-39a540202)
[![GitHub](https://img.shields.io/badge/GitHub-100000?style=flat-square&logo=github&logoColor=white)](https://github.com/Adiie0001)
[![Email](https://img.shields.io/badge/Email-D14836?style=flat-square&logo=gmail&logoColor=white)](mailto:adiiimaisuriya94@gmail.com)

---

## 🎯 **About the Developer**

**Aditya Maisuriya** - Full-Stack ASP.NET Core & C# Developer  
📍 Valsad, India  
💼 2+ Years of Experience in Enterprise Applications  
🚀 Specialized in SaaS & ERP Solutions  
⚡ Achieved 99.9% uptime on production systems

---

## 🌟 **Project Stats**

- **Lines of Code:** 2,000+
- **Controllers:** 4 (Clients, Locums, Bookings, Holidays)
- **API Endpoints:** 10+
- **Database Entities:** 5
- **Features:** RBAC, Automated Matching, AJAX, Identity

---

⭐ **If you find this project useful, please star it!** ⭐

---

**Note**: This project was recreated from documentation as part of a portfolio demonstration of ASP.NET Core MVC development capabilities.
