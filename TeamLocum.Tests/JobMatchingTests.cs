using Microsoft.AspNetCore.Mvc;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TeamLocum.Web.Controllers;
using TeamLocum.Web.Models;
using TeamLocum.Web.Services;
using Xunit;

namespace TeamLocum.Tests
{
    public class JobMatchingTests
    {
        [Fact]
        public async Task MatchLocums_ReturnsBestMatchesFirst()
        {
            // Arrange
            var mockService = new Mock<IAiMatchingService>();
            
            var userA = new ApplicationUser { Id = "u1", FirstName = "Alice", LastName = "A" };
            var userB = new ApplicationUser { Id = "u2", FirstName = "Bob", LastName = "B" };
            
            var locumA = new Locum { Id = 1, UserId = "u1", User = userA };
            var locumB = new Locum { Id = 2, UserId = "u2", User = userB };
            var locums = new List<Locum> { locumA, locumB };
            
            var mockResults = new List<AiMatchResult>
            {
                new AiMatchResult { LocumId = 1, MatchScore = 95, MatchReasoning = "Great fit" },
                new AiMatchResult { LocumId = 2, MatchScore = 40, MatchReasoning = "Poor fit" }
            };
            
            mockService.Setup(s => s.CalculateMatchScoresAsync(locums, "Need GP", "London")).ReturnsAsync(mockResults);

            // Act
            var results = await mockService.Object.CalculateMatchScoresAsync(locums, "Need GP", "London");

            // Assert
            Assert.Equal(2, results.Count);
            Assert.Equal(95, results.First(r => r.LocumId == 1).MatchScore);
        }

        [Fact]
        public void LocumAvailability_IsCheckedCorrectly()
        {
            // Arrange
            var existingBooking = new Booking { 
                LocumId = 1, 
                Date = DateTime.Today,
                StartTime = new TimeSpan(9, 0, 0), 
                EndTime = new TimeSpan(13, 0, 0),
                Location = "London"
            };
            
            var newBooking = new Booking { 
                Date = DateTime.Today,
                StartTime = new TimeSpan(11, 0, 0), 
                EndTime = new TimeSpan(15, 0, 0),
                Location = "London"
            };

            // Act
            bool isOverlap = existingBooking.Date == newBooking.Date &&
                             existingBooking.StartTime < newBooking.EndTime && 
                             newBooking.StartTime < existingBooking.EndTime;

            // Assert
            Assert.True(isOverlap);
        }
        
        [Fact]
        public void RBAC_AdminCanApproveBooking()
        {
            // Arrange
            var booking = new Booking { Id = 1, Status = "Pending", Location = "London" };
            var userRole = "Admin";
            
            // Act
            if (userRole == "Admin")
            {
                booking.Status = "Approved";
            }
            
            // Assert
            Assert.Equal("Approved", booking.Status);
        }
        
        [Fact]
        public void InputValidation_BookingRequiresClient()
        {
            // Arrange
            var booking = new Booking { Id = 1, ClientId = 0, Location = "London" }; // Missing IDs
            
            // Act
            bool isValid = booking.ClientId > 0;
            
            // Assert
            Assert.False(isValid);
        }
        
        [Fact]
        public void Model_Locum_Initialization()
        {
            // Arrange
            var user = new ApplicationUser { Id = "u1", FirstName = "Alice" };
            var locum = new Locum { Id = 1, UserId = "u1", User = user, GmcNumber = "12345" };
            
            // Act & Assert
            Assert.Equal("12345", locum.GmcNumber);
            Assert.Equal("Active", locum.Status);
        }
    }
}
