// Controllers/ProcessorController.cs
using JakeScerriPFTC_Assignment.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace JakeScerriPFTC_Assignment.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    // Remove the authorization attribute to allow unauthenticated access
    // [Authorize(Roles = "Technician")] 
    public class ProcessorController : ControllerBase
    {
        private readonly TicketProcessorService _ticketProcessorService;
        private readonly ILogger<ProcessorController> _logger;

        public ProcessorController(
            TicketProcessorService ticketProcessorService,
            ILogger<ProcessorController> logger)
        {
            _ticketProcessorService = ticketProcessorService;
            _logger = logger;
        }

        [HttpPost("process-tickets")]
        public async Task<IActionResult> ProcessTickets()
        {
            try
            {
                _logger.LogInformation("Manual ticket processing triggered at {Time}", DateTime.UtcNow);
                Console.WriteLine("Processing tickets function executed");
                
                var result = await _ticketProcessorService.ProcessTicketsAsync();
                
                _logger.LogInformation("Ticket processing completed successfully");
                return Ok(new { 
                    success = true, 
                    message = "Ticket processing completed successfully",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing tickets");
                Console.WriteLine($"Error: {ex.Message}");
                return StatusCode(500, new { 
                    success = false, 
                    error = ex.Message 
                });
            }
        }
        
        // Add a simple health check endpoint
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
        }
    }
}