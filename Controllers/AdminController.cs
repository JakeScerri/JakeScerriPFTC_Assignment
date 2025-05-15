using JakeScerriPFTC_Assignment.Models;
using JakeScerriPFTC_Assignment.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace JakeScerriPFTC_Assignment.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Technician")] // Admin functions are available to technicians
    public class AdminController : ControllerBase
    {
        private readonly FirestoreService _firestoreService;
        private readonly StorageService _storageService;
        private readonly PubSubService _pubSubService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            FirestoreService firestoreService,
            StorageService storageService,
            PubSubService pubSubService,
            ILogger<AdminController> logger)
        {
            _firestoreService = firestoreService;
            _storageService = storageService;
            _pubSubService = pubSubService;
            _logger = logger;
        }

        // Get all technicians
        [HttpGet("technicians")]
        public async Task<IActionResult> GetAllTechnicians()
        {
            try
            {
                _logger.LogInformation("Admin requesting all technicians");
                var technicians = await _firestoreService.GetTechniciansAsync();
                
                return Ok(technicians);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting technicians");
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }
        
        // Update a user's role (promote to technician or demote to user)
        [HttpPut("users/{email}/role")]
        public async Task<IActionResult> UpdateUserRole(string email, [FromBody] UpdateRoleModel model)
        {
            try
            {
                _logger.LogInformation($"Admin updating user {email} role to {model.Role}");
                
                var user = await _firestoreService.SaveUserAsync(email, model.Role);
                
                return Ok(new { 
                    message = $"User {email} role updated to {model.Role}",
                    user
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating user {email} role");
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }
        
        // Archive old tickets
        [HttpPost("tickets/archive")]
        public async Task<IActionResult> ArchiveOldTickets()
        {
            try
            {
                string technicianEmail = User.FindFirstValue(ClaimTypes.Email);
                _logger.LogInformation($"Admin {technicianEmail} archiving old tickets");
                
                // In a real implementation, you would find and archive old tickets
                // This is a placeholder for demonstration
                
                return Ok(new { 
                    message = "Ticket archiving functionality would be implemented here"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error archiving tickets");
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }
    }

    // Model for updating user role
    public class UpdateRoleModel
    {
        public UserRole Role { get; set; }
    }
}