using JakeScerriPFTC_Assignment.Models;
using JakeScerriPFTC_Assignment.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Linq;
using System.Threading.Tasks;

namespace JakeScerriPFTC_Assignment.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Technician")] // Only technicians can access this controller
    public class TechniciansController : ControllerBase
    {
        private readonly FirestoreService _firestoreService;
        private readonly RedisService _redisService;
        private readonly PubSubService _pubSubService;
        private readonly ILogger<TechniciansController> _logger;

        public TechniciansController(
            FirestoreService firestoreService, 
            RedisService redisService,
            PubSubService pubSubService,
            ILogger<TechniciansController> logger)
        {
            _firestoreService = firestoreService;
            _redisService = redisService;
            _pubSubService = pubSubService;
            _logger = logger;
        }

        [HttpGet("tickets")]
        public async Task<IActionResult> GetAllTickets()
        {
            try
            {
                string technicianEmail = User.FindFirstValue(ClaimTypes.Email);
                _logger.LogInformation($"Technician {technicianEmail} requesting all tickets");
                
                // Get open tickets from Redis cache (KU4.1.a - Read from cache)
                var tickets = await _redisService.GetOpenTicketsAsync();
                
                _logger.LogInformation($"Retrieved {tickets.Count} open tickets from Redis cache");
                
                return Ok(new { 
                    success = true,
                    tickets = tickets
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tickets for technician");
                return StatusCode(500, new { 
                    success = false, 
                    error = ex.Message 
                });
            }
        }

       [HttpGet("tickets/priority/{priority}")]
        public async Task<IActionResult> GetTicketsByPriority(string priority)
        {
            try
            {
                string technicianEmail = User.FindFirstValue(ClaimTypes.Email);
                
                // Parse the priority
                if (!Enum.TryParse<TicketPriority>(priority, true, out var ticketPriority))
                {
                    return BadRequest(new { 
                        success = false, 
                        error = "Invalid priority. Valid values are High, Medium, Low." 
                    });
                }
                
                _logger.LogInformation($"Technician {technicianEmail} requesting {priority} priority tickets");
                
                // Get tickets by priority from Redis cache
                var tickets = await _redisService.GetTicketsByPriorityAsync(ticketPriority);
                
                _logger.LogInformation($"Retrieved {tickets.Count} {priority} priority tickets from Redis cache");
                
                return Ok(new { 
                    success = true,
                    tickets = tickets
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting {priority} priority tickets for technician");
                return StatusCode(500, new { 
                    success = false, 
                    error = ex.Message 
                });
            }
        }

        [HttpPost("tickets/{id}/close")]
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
                
                // Close the ticket in Redis
                await _redisService.CloseTicketAsync(id, technicianEmail);
                
                // Check if ticket is more than a week old
                if ((DateTime.UtcNow - ticket.DateUploaded).TotalDays > 7)
                {
                    // Archive to Firestore
                    await _firestoreService.ArchiveTicketAsync(ticket, technicianEmail);
                    _logger.LogInformation($"Ticket {id} archived to Firestore (older than 1 week)");
                }
                
                // Acknowledge in PubSub to remove the message
                await _pubSubService.AcknowledgeTicketAsync(id, ticket.Priority);
                
                _logger.LogInformation($"Ticket {id} closed by {technicianEmail} and acknowledged in PubSub");
                
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

        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            try
            {
                string technicianEmail = User.FindFirstValue(ClaimTypes.Email);
                _logger.LogInformation($"Technician {technicianEmail} accessing dashboard");
                
                // Get open tickets from Redis cache
                var allTickets = await _redisService.GetOpenTicketsAsync();
                
                // Group tickets by priority
                var highPriorityCount = allTickets.Count(t => t.Priority == TicketPriority.High);
                var mediumPriorityCount = allTickets.Count(t => t.Priority == TicketPriority.Medium);
                var lowPriorityCount = allTickets.Count(t => t.Priority == TicketPriority.Low);
                
                return Ok(new { 
                    success = true,
                    ticketCounts = new {
                        total = allTickets.Count,
                        high = highPriorityCount,
                        medium = mediumPriorityCount,
                        low = lowPriorityCount
                    },
                    recentTickets = allTickets.OrderByDescending(t => t.DateUploaded).Take(5)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading technician dashboard");
                return StatusCode(500, new { 
                    success = false, 
                    error = ex.Message 
                });
            }
        }
    }
} 