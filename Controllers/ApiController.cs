// Controllers/ApiController.cs
using JakeScerriPFTC_Assignment.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace JakeScerriPFTC_Assignment.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ApiController : ControllerBase
    {
        private readonly TicketProcessorService _ticketProcessor;
        private readonly ILogger<ApiController> _logger;

        public ApiController(
            TicketProcessorService ticketProcessor,
            ILogger<ApiController> logger)
        {
            _ticketProcessor = ticketProcessor;
            _logger = logger;
        }

        [HttpPost("process-tickets")]
        [Authorize(Roles = "Technician")] // Restrict to technicians for testing
        public async Task<IActionResult> ProcessTickets()
        {
            try
            {
                _logger.LogInformation("Manual trigger of ticket processing");
                await _ticketProcessor.ProcessTicketsAsync();
                return Ok(new { message = "Ticket processing triggered successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing tickets");
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }
    }
}