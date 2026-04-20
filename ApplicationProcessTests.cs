using DUIWA_Tests.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Security.Claims;
using Team01_DUIWA.Controllers;
using Team01_DUIWA.Data;
using Team01_DUIWA.Models;
using MySqlConnector;

namespace DUIWA_Tests.Tests
{
    public class ApplicationProcessTests
    {
        private readonly DUIWADbContext _context;
        private readonly IConfiguration _config;

        public ApplicationProcessTests()
        {
            _config = new ConfigurationBuilder()
                .AddUserSecrets<ApplicationProcessTests>()
                .Build();

            var connectionString = _config.GetConnectionString("DefaultConnection");

            var options = new DbContextOptionsBuilder<DUIWADbContext>()
                .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 44)))
                .Options;

            _context = new DUIWADbContext(options);
        }

        private AccountController CreateController(int userId, string role = "Driver")
        {
            var claims = new List<Claim> 
            { 
                new Claim("UserId", userId.ToString()),
                new Claim(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, "TestAuthType");
            var principal = new ClaimsPrincipal(identity);

            var session = new MockSession();
            var httpContext = new DefaultHttpContext
            {
                User = principal,
                Session = session
            };

            var mockEmailService = new Mock<IEmailService>();
            var mockTempData = new Mock<ITempDataDictionary>();

            var controller = new AccountController(null, _config, _context, mockEmailService.Object, null)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
                TempData = mockTempData.Object
            };

            return controller;
        }

        [Fact]
        public async Task MassApply_WhenAlreadySponsored_DoesNotCreateDuplicateApplication()
        {
            int testDriverId = 1; 
            int testSponsorId = 1;
            var controller = CreateController(testDriverId);

            var selectedSponsors = new List<int> { testSponsorId };

            var result = await controller.MassApply(selectedSponsors) as RedirectToActionResult;

            var appExists = await _context.DriverApplicationModel
                .AnyAsync(a => a.DriverID == testDriverId && a.SponsorID == testSponsorId && a.CurrentStatus == 0);

            Assert.False(appExists, "Application should not have been created for an existing sponsor.");
            Assert.Equal("ViewSponsorOrgs", result?.ActionName);
        }

        [Fact]
        public async Task MassApply_ValidSponsor_CreatesPendingApplication()
        {
            int testDriverId = 1;
            int testSponsorId = 2; 
            var controller = CreateController(testDriverId);
            
            var selectedSponsors = new List<int> { testSponsorId };

            var result = await controller.MassApply(selectedSponsors) as RedirectToActionResult;

            var newApp = await _context.DriverApplicationModel
                .FirstOrDefaultAsync(a => a.DriverID == testDriverId && a.SponsorID == testSponsorId);

            Assert.NotNull(newApp);
            Assert.Equal(0, newApp.CurrentStatus);
            Assert.Equal("ViewApplications", result?.ActionName);
        }

        [Fact]
        public async Task AcceptSponsorOffer_CreatesDriverSponsorLink()
        {
            int testDriverId = 1;
            int testAppId = 1; 
            var controller = CreateController(testDriverId);

            var result = await controller.AcceptSponsorOffer(testAppId) as RedirectToActionResult;

            var app = await _context.DriverApplicationModel.FindAsync(testAppId);
            var linkExists = await _context.DriverSponsorModel
                .AnyAsync(l => l.DriverID == testDriverId && l.SponsorID == app.SponsorID);

            Assert.Equal(4, app?.CurrentStatus);
            Assert.True(linkExists, "Bridge table link should be created upon acceptance.");
            Assert.Equal("ViewApplications", result?.ActionName);
        }

        [Fact]
        public async Task RemoveSponsorFromDriver_DeletesBridgeTableLink()
        {
            int adminId = 99; 
            int testDriverId = 1;
            int testSponsorId = 1;
            var controller = CreateController(adminId, "Admin");

            var result = await controller.RemoveSponsorFromDriver(testDriverId, testSponsorId) as RedirectToActionResult;

            var linkExists = await _context.DriverSponsorModel
                .AnyAsync(l => l.DriverID == testDriverId && l.SponsorID == testSponsorId);

            Assert.False(linkExists, "Bridge table link should be deleted when removed by Admin.");
            Assert.Equal("EditDrivers", result?.ActionName);
        }

        [Fact]
        public async Task SelectSponsor_WhenDroppedBySponsor_BlocksStoreAccess()
        {
            int testDriverId = 1;
            int testSponsorId = 1;
            var controller = CreateController(testDriverId);

            var result = await controller.SelectSponsor(testSponsorId) as RedirectToActionResult;

            var sessionSponsorId = controller.HttpContext.Session.GetInt32("ActiveSponsorId");

            Assert.Null(sessionSponsorId);
            Assert.Equal("Dashboard", result?.ActionName);
        }
    }
}