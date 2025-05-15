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

        public TicketsController(
            StorageService storageService,
            FirestoreService firestoreService,
            PubSubService pubSubService,
            EmailService emailService,
            ILogger<TicketsController> logger)
        {
            _storageService = storageService;
            _firestoreService = firestoreService;
            _pubSubService = pubSubService;
            _emailService = emailService;
            _logger = logger;
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
                
                return Ok(new 
                { 
                    message = "Ticket created successfully", 
                    ticketId = ticket.Id, 
                    messageId,
                    imageUrls
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating ticket");
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }

        [HttpGet("{id}")]
        public IActionResult GetTicket(string id)
        {
            // Get current user role
            bool isTechnician = User.IsInRole("Technician");
            string userEmail = User.FindFirstValue(ClaimTypes.Email);
            
            _logger.LogInformation($"User {userEmail} (Technician: {isTechnician}) accessing ticket {id}");
            
            // In a real implementation, you would:
            // 1. Fetch the ticket from database
            // 2. Check if user is allowed to view it (technician or ticket owner)
            
            return Ok(new { 
                message = $"Viewing ticket {id}",
                userRole = isTechnician ? "Technician" : "User",
                userEmail = userEmail
            });
        }
        
        [HttpPost("{id}/close")]
        [Authorize(Roles = "Technician")] // Only technicians can close tickets
        public async Task<IActionResult> CloseTicket(string id)
        {
            try
            {
                string technicianEmail = User.FindFirstValue(ClaimTypes.Email);
                
                _logger.LogInformation($"Technician {technicianEmail} closing ticket {id}");
                
                // In a real implementation, you would:
                // 1. Fetch the ticket from cache/database
                // 2. Update its status to Closed
                // 3. If it's been open more than a week, archive it
                
                return Ok(new { 
                    message = $"Ticket {id} closed successfully by {technicianEmail}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error closing ticket {id}");
                return StatusCode(500, $"An error occurred: {ex.Message}");
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
                
                // In a real implementation, you would:
                // 1. Fetch the ticket from database
                // 2. Send email notification
                
                // For testing, create a simulated ticket
                var ticket = new Ticket
                {
                    Id = id,
                    Title = "Test Ticket",
                    Description = "This is a test ticket for email notification",
                    UserEmail = "testuser@example.com",
                    Priority = TicketPriority.High,
                    DateUploaded = DateTime.UtcNow,
                    Status = TicketStatus.Open
                };
                
                // Send email notification
                await _emailService.SendTicketNotificationAsync(ticket);
                
                return Ok(new { 
                    message = $"Email notification sent for ticket {id}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending notification for ticket {id}");
                return StatusCode(500, $"An error occurred: {ex.Message}");
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