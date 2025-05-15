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
    [Authorize(Roles = "Technician")] // Only technicians can manually trigger processing
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
                _logger.LogInformation("Manual ticket processing triggered");
                
                var result = await _ticketProcessorService.ProcessTicketsAsync();
                
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error manually processing tickets");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}