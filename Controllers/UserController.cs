// Controllers/UsersController.cs
using JakeScerriPFTC_Assignment.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly RedisService _redisService;
        private readonly ILogger<UsersController> _logger;
        private readonly PubSubService _pubSubService;

        public UsersController(
            FirestoreService firestoreService, 
            RedisService redisService,
            PubSubService pubSubService, // Add this parameter
            ILogger<UsersController> logger)
        {
            _firestoreService = firestoreService;
            _redisService = redisService;
            _pubSubService = pubSubService; // Assign the injected service
            _logger = logger;
        }

        [HttpGet("tickets")]
        public async Task<IActionResult> GetMyTickets()
        {
            try
            {
                // Get current user's email
                string userEmail = User.FindFirstValue(ClaimTypes.Email);
                _logger.LogInformation($"User {userEmail} requesting their tickets");
                
                // Get all tickets from cache
                var allTickets = await _redisService.GetOpenTicketsAsync();
                
                // Filter to show only this user's tickets
                var userTickets = allTickets.Where(t => t.UserEmail == userEmail).ToList();
                
                _logger.LogInformation($"Retrieved {userTickets.Count} tickets for user {userEmail}");
                
                return Ok(new { 
                    success = true,
                    tickets = userTickets
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user tickets");
                return StatusCode(500, new { 
                    success = false, 
                    error = ex.Message 
                });
            }
        }

        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            try
            {
                string userEmail = User.FindFirstValue(ClaimTypes.Email);
                _logger.LogInformation($"User {userEmail} requesting profile");
                
                var user = await _firestoreService.GetUserByEmailAsync(userEmail);
                
                if (user == null)
                {
                    _logger.LogWarning($"User {userEmail} not found in database");
                    return NotFound(new {
                        success = false,
                        error = "User profile not found"
                    });
                }
                
                return Ok(new
                {
                    success = true,
                    profile = new {
                        email = user.Email,
                        role = user.Role.ToString(),
                        createdAt = user.CreatedAt
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user profile");
                return StatusCode(500, new { 
                    success = false, 
                    error = ex.Message 
                });
            }
        }
        
        // User can also close their own tickets
        [HttpPost("tickets/{id}/close")]
        public async Task<IActionResult> CloseTicket(string id)
        {
            try
            {
                string userEmail = User.FindFirstValue(ClaimTypes.Email);
                _logger.LogInformation($"User {userEmail} closing ticket {id}");
                
                // Get the ticket from Redis
                var ticket = await _redisService.GetTicketAsync(id);
                if (ticket == null)
                {
                    return NotFound(new { 
                        success = false, 
                        error = $"Ticket {id} not found" 
                    });
                }
                
                // Users can only close their own tickets
                if (ticket.UserEmail != userEmail)
                {
                    _logger.LogWarning($"User {userEmail} attempted to close ticket {id} belonging to {ticket.UserEmail}");
                    return Forbid();
                }
                
                // Close the ticket in Redis
                await _redisService.CloseTicketAsync(id, userEmail);
                
                // Check if ticket is more than a week old
                if ((DateTime.UtcNow - ticket.DateUploaded).TotalDays > 7)
                {
                    // Archive to Firestore
                    await _firestoreService.ArchiveTicketAsync(ticket, userEmail);
                    _logger.LogInformation($"Ticket {id} archived to Firestore (older than 1 week)");
                }
                
                // Acknowledge in PubSub to remove the message
                await _pubSubService.AcknowledgeTicketAsync(id, ticket.Priority);
                
                _logger.LogInformation($"Ticket {id} closed by {userEmail} and acknowledged in PubSub");
                
                return Ok(new { 
                    success = true,
                    message = $"Ticket {id} closed successfully" 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error closing ticket {id}");
                return StatusCode(500, new { 
                    success = false, 
                    error = ex.Message 
                });
            }
        }
    }
}