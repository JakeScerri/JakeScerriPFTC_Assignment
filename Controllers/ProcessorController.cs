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
    [Authorize(Roles = "Technician")]
    public class ProcessorController : ControllerBase
    {
        private readonly TicketProcessorService _processorService;
        private readonly ILogger<ProcessorController> _logger;

        public ProcessorController(
            TicketProcessorService processorService,
            ILogger<ProcessorController> logger)
        {
            _processorService = processorService;
            _logger = logger;
        }

        [HttpPost("process-tickets")]
        public async Task<IActionResult> ProcessTickets()
        {
            try
            {
                await _processorService.ProcessTicketsAsync();
                return Ok(new { message = "Tickets processed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing tickets");
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }
    }
}