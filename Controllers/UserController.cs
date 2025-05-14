using JakeScerriPFTC_Assignment.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Threading.Tasks;
using JakeScerriPFTC_Assignment.Services;

namespace JakeScerriPFTC_Assignment.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "User,Technician")] // Both regular users and technicians can access
    public class UsersController : ControllerBase
    {
        private readonly FirestoreService _firestoreService;
        private readonly ILogger<UsersController> _logger;

        public UsersController(FirestoreService firestoreService, ILogger<UsersController> logger)
        {
            _firestoreService = firestoreService;
            _logger = logger;
        }

        [HttpGet("tickets")]
        public async Task<IActionResult> GetMyTickets()
        {
            // Get current user's email
            string userEmail = User.FindFirstValue(ClaimTypes.Email);
            
            // This controller would typically fetch tickets for the current user
            // For now, we'll just return a simple response
            return Ok(new { message = $"This would show tickets for user: {userEmail}" });
        }

        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            string userEmail = User.FindFirstValue(ClaimTypes.Email);
            var user = await _firestoreService.GetUserByEmailAsync(userEmail);
            
            return Ok(new
            {
                email = user?.Email,
                role = user?.Role.ToString(),
                createdAt = user?.CreatedAt
            });
        }
    }
}