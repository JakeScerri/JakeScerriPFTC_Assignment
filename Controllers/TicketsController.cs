// Controllers/TicketsController.cs
using JakeScerriPFTC_Assignment.Models;
using JakeScerriPFTC_Assignment.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace JakeScerriPFTC_Assignment.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Requires authentication, but doesn't restrict by role
    public class TicketsController : ControllerBase
    {
        private readonly StorageService _storageService;
        private readonly FirestoreService _firestoreService;
        private readonly PubSubService _pubSubService;
        private readonly EmailService _emailService;
        private readonly ILogger<TicketsController> _logger;
        private readonly RedisService _redisService;

        public TicketsController(
            StorageService storageService,
            FirestoreService firestoreService,
            PubSubService pubSubService,
            EmailService emailService,
            RedisService redisService,
            ILogger<TicketsController> logger)
        {
            _storageService = storageService;
            _firestoreService = firestoreService;
            _pubSubService = pubSubService;
            _emailService = emailService;
            _logger = logger;
            _redisService = redisService;
        }

        [HttpPost]
        public async Task<IActionResult> CreateTicket([FromForm] TicketCreateModel model)
        {
            try
            {
                // Get email from authenticated user
                string userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "anonymous@example.com";
                
                _logger.LogInformation($"Creating ticket for user: {userEmail}");
                
                // Get the current user and their role before saving
                var existingUser = await _firestoreService.GetUserByEmailAsync(userEmail);
                
                // Ensure user exists in Firestore with their CURRENT role preserved
                // Pass null as the role to ensure we don't change it
                await _firestoreService.SaveUserAsync(userEmail, existingUser?.Role);
                
                // Upload screenshots to Cloud Storage (AA2.1.c & KU4.3.a)
                var imageUrls = new List<string>();
                if (model.Screenshots != null && model.Screenshots.Count > 0)
                {
                    _logger.LogInformation($"Uploading {model.Screenshots.Count} screenshots");
                    imageUrls = await _storageService.UploadFilesAsync(model.Screenshots, userEmail);
                }
                
                // Create ticket object
                var ticket = new Ticket
                {
                    Title = model.Title,
                    Description = model.Description,
                    UserEmail = userEmail,
                    Priority = model.Priority,
                    ImageUrls = imageUrls,
                    Status = TicketStatus.Open,
                    DateUploaded = DateTime.UtcNow
                };
                
                _logger.LogInformation($"Publishing ticket with priority: {ticket.Priority}");
                
                // Publish ticket to PubSub with priority attribute (AA2.1.a & AA2.1.b)
                var messageId = await _pubSubService.PublishTicketAsync(ticket);
                
                // Also save to Redis for immediate access
                await _redisService.SaveTicketAsync(ticket);
                
                return Ok(new 
                { 
                    success = true,
                    message = "Ticket created successfully", 
                    ticketId = ticket.Id, 
                    messageId,
                    imageUrls
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating ticket");
                return StatusCode(500, new { 
                    success = false, 
                    error = ex.Message
                });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetTicket(string id)
        {
            try 
            {
                // Get current user role
                bool isTechnician = User.IsInRole("Technician");
                string userEmail = User.FindFirstValue(ClaimTypes.Email);
                
                _logger.LogInformation($"User {userEmail} (Technician: {isTechnician}) accessing ticket {id}");
                
                // Fetch the ticket from Redis
                var ticket = await _redisService.GetTicketAsync(id);
                
                if (ticket == null)
                {
                    return NotFound(new { 
                        success = false, 
                        error = $"Ticket {id} not found" 
                    });
                }
                
                // Check if user is allowed to view this ticket
                // Technicians can view all tickets, users can only view their own
                if (!isTechnician && ticket.UserEmail != userEmail)
                {
                    return Forbid();
                }
                
                return Ok(new { 
                    success = true,
                    ticket = ticket
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting ticket {id}");
                return StatusCode(500, new { 
                    success = false, 
                    error = ex.Message
                });
            }
        }
        
        [HttpPost("{id}/close")]
        [Authorize(Roles = "Technician")] // Only technicians can close tickets
        public async Task<IActionResult> CloseTicket(string id)
        {
            try
            {
                string technicianEmail = User.FindFirstValue(ClaimTypes.Email);
        
                _logger.LogInformation($"Technician {technicianEmail} closing ticket {id}");
        
                // Get the ticket from Redis
                var ticket = await _redisService.GetTicketAsync(id);
                if (ticket == null)
                {
                    return NotFound(new { 
                        success = false, 
                        error = $"Ticket {id} not found" 
                    });
                }
        
                // Update ticket status
                ticket.Status = TicketStatus.Closed;
        
                // Close the ticket in Redis cache
                await _redisService.CloseTicketAsync(id, technicianEmail);
        
                // Check if ticket is more than a week old
                if ((DateTime.UtcNow - ticket.DateUploaded).TotalDays > 7)
                {
                    // Archive to Firestore (AA4.2.b)
                    await _firestoreService.ArchiveTicketAsync(ticket, technicianEmail);
                    _logger.LogInformation($"Ticket {id} archived to Firestore (older than 1 week)");
                }
                
                // Acknowledge the message in PubSub to remove it
                await _pubSubService.AcknowledgeTicketAsync(id, ticket.Priority);
                _logger.LogInformation($"Ticket {id} acknowledged in PubSub");
        
                return Ok(new { 
                    success = true,
                    message = $"Ticket {id} closed successfully by {technicianEmail}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error closing ticket {id}");
                return StatusCode(500, new {
                    success = false,
                    error = $"An error occurred: {ex.Message}"
                });
            }
        }

        [HttpPost("{id}/notify")]
        [Authorize(Roles = "Technician")]
        public async Task<IActionResult> NotifyTicket(string id)
        {
            try
            {
                string technicianEmail = User.FindFirstValue(ClaimTypes.Email);
                
                _logger.LogInformation($"Technician {technicianEmail} sending notification for ticket {id}");
                
                // Get the ticket from Redis
                var ticket = await _redisService.GetTicketAsync(id);
                if (ticket == null)
                {
                    return NotFound(new { 
                        success = false, 
                        error = $"Ticket {id} not found" 
                    });
                }
                
                // Send email notification
                await _emailService.SendTicketNotificationAsync(ticket);
                
                return Ok(new { 
                    success = true,
                    message = $"Email notification sent for ticket {id}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending notification for ticket {id}");
                return StatusCode(500, new {
                    success = false,
                    error = $"An error occurred: {ex.Message}"
                });
            }
        }
    }

    public class TicketCreateModel
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public TicketPriority Priority { get; set; }
        public List<IFormFile> Screenshots { get; set; } = new List<IFormFile>();
    }
}