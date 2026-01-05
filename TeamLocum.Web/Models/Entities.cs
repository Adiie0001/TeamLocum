using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TeamLocum.Web.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string? UserType { get; set; } // "Admin", "Client", "Locum"
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class Client
    {
        [Key]
        public int Id { get; set; }
        
        [ForeignKey("User")]
        public string UserId { get; set; }
        public ApplicationUser User { get; set; }

        public string CompanyName { get; set; }
        public string? CarasId { get; set; }
        public string Status { get; set; } = "Awaiting Review";
        public bool IsAccredited { get; set; }
    }

    public class Locum
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("User")]
        public string UserId { get; set; }
        public ApplicationUser User { get; set; }

        public string? CarasId { get; set; }
        public string Status { get; set; } = "Active";
        public string? ResumePath { get; set; }
        public string? GmcNumber { get; set; }
    }

    public class Booking
    {
        [Key]
        public int Id { get; set; }

        public int ClientId { get; set; }
        public Client Client { get; set; }

        public int? LocumId { get; set; }
        public Locum? Locum { get; set; }

        public DateTime Date { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal RatePerHour { get; set; }
        
        public string Location { get; set; }
        public string Status { get; set; } = "Open"; // Open, Filled, Completed, Cancelled
        public string? Notes { get; set; }
    }

    public class Holiday
    {
        [Key]
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string Details { get; set; }
    }
}
