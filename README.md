# Team Locum - Healthcare Staffing Management System

A comprehensive web-based application designed to streamline the management of locum doctor bookings for healthcare organizations. Built with ASP.NET Core MVC, this system facilitates seamless coordination between healthcare clients and medical professionals.

## 🌟 Features

### For Administrators
- **Account Management**: Review and approve/reject client and locum registrations
- **Masquerade Functionality**: Impersonate client accounts to provide support
- **Holiday Master**: Manage public holidays for accurate rate calculations
- **Booking Oversight**: Monitor all bookings across the platform

### For Clients (Healthcare Organizations)
- **Job Posting**: Create booking requests for locum doctors
- **Locum Matching**: View and select suitable locum doctors
- **Booking Management**: Track active and historical bookings
- **Dashboard**: Real-time overview of staffing needs

### For Locums (Medical Professionals)
- **Profile Management**: Maintain professional credentials and availability
- **Job Applications**: Apply for suitable booking opportunities
- **Schedule Tracking**: View confirmed and pending assignments

## 🛠️ Technology Stack

- **Framework**: ASP.NET Core 8 MVC
- **Language**: C#
- **Database**: Microsoft SQL Server (with Entity Framework Core)
- **Authentication**: ASP.NET Core Identity
- **Frontend**: Razor Views, Bootstrap 5, jQuery
- **ORM**: Entity Framework Core 8

## 📋 Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [SQL Server](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) (LocalDB or Express Edition)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) or [VS Code](https://code.visualstudio.com/)

## 🚀 Getting Started

### 1. Clone the Repository
```bash
git clone https://github.com/Adiie0001/TeamLocum.git
cd TeamLocum
```

### 2. Configure Database Connection
Update the connection string in `TeamLocum.Web/appsettings.json`:
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=TeamLocumDb;Trusted_Connection=True;MultipleActiveResultSets=true"
}
```

### 3. Restore Dependencies
```bash
cd TeamLocum.Web
dotnet restore
```

### 4. Apply Database Migrations
```bash
dotnet ef database update
```

### 5. Run the Application
```bash
dotnet run
```

Navigate to `https://localhost:5001` in your browser.

## 📁 Project Structure

```
TeamLocum/
├── TeamLocum.Web/
│   ├── Controllers/          # MVC Controllers
│   │   ├── ClientsController.cs
│   │   ├── LocumsController.cs
│   │   ├── BookingsController.cs
│   │   └── HolidaysController.cs
│   ├── Data/                 # Database Context
│   │   └── ApplicationDbContext.cs
│   ├── Models/               # Data Models
│   │   └── Entities.cs
│   ├── Views/                # Razor Views
│   │   ├── Clients/
│   │   ├── Locums/
│   │   ├── Bookings/
│   │   └── Holidays/
│   ├── wwwroot/              # Static Files
│   └── Program.cs            # Application Entry Point
└── TeamLocum.sln             # Solution File
```

## 🗄️ Database Schema

### Core Entities
- **ApplicationUser**: Extended Identity user with UserType, FirstName, LastName
- **Client**: Healthcare organization details and accreditation status
- **Locum**: Medical professional profile and availability
- **Booking**: Job shifts with date, time, location, and rates
- **Holiday**: Master list of public holidays

## 🔐 User Roles

1. **Admin**: Full system access, account approval, masquerade capability
2. **Client**: Post jobs, manage bookings, select locums
3. **Locum**: Apply for jobs, manage availability, track assignments

## 🎯 Key Features Implementation

### Role-Based Access Control (RBAC)
User roles are managed through the `UserType` field in `ApplicationUser`, enabling differentiated access across the platform.

### Account Approval Workflow
New client and locum registrations require admin approval before gaining full system access.

### Booking Management
Comprehensive shift management with:
- Date and time selection
- Location specification
- Hourly rate calculation
- Assignment tracking

## 🔧 Development

### Adding New Migrations
```bash
dotnet ef migrations add MigrationName
dotnet ef database update
```

### Building for Production
```bash
dotnet publish -c Release -o ./publish
```

## 📝 License

This project is available for educational and portfolio purposes.

## 👤 Author

**Aditya Maisuriya**
- GitHub: [@Adiie0001](https://github.com/Adiie0001)
- Portfolio: [adityamaisuriya.pages.dev](https://adityamaisuriya.pages.dev/)

## 🤝 Contributing

This is a portfolio project. Feel free to fork and adapt for your own use.

## 🚧 Future Enhancements

- Advanced job matching algorithm
- Real-time notifications
- Mobile responsive design improvements
- Integration with external credentialing systems (CARAS)
- Payment processing integration
- Reporting and analytics dashboard
- API development for third-party integrations

---

**Note**: This project was recreated from documentation as part of a portfolio demonstration of ASP.NET Core MVC development capabilities.
