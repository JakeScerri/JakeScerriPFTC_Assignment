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
    // No authorization to allow Cloud Scheduler to trigger it
    public class ProcessorController : ControllerBase
    {
        private readonly TicketProcessorService _ticketProcessorService;
        private readonly RedisService _redisService;
        private readonly ILogger<ProcessorController> _logger;

        public ProcessorController(
            TicketProcessorService ticketProcessorService,
            RedisService redisService,
            ILogger<ProcessorController> logger)
        {
            _ticketProcessorService = ticketProcessorService;
            _redisService = redisService;
            _logger = logger;
        }

        [HttpPost("process-tickets")]
        public async Task<IActionResult> ProcessTickets()
        {
            try
            {
                _logger.LogInformation("Ticket processing triggered at {Time}", DateTime.UtcNow);
                
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
                return StatusCode(500, new { 
                    success = false, 
                    error = ex.Message 
                });
            }
        }
        
        // This endpoint allows manual testing of ticket processing
        [HttpGet("manual-trigger")]
        public async Task<IActionResult> ManualTrigger()
        {
            try
            {
                _logger.LogInformation("Manual ticket processing triggered at {Time}", DateTime.UtcNow);
                
                var result = await _ticketProcessorService.ProcessTicketsAsync();
                
                _logger.LogInformation("Manual ticket processing completed successfully");
                return Ok(new { 
                    success = true, 
                    message = "Ticket processing completed successfully",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing tickets manually");
                return StatusCode(500, new { 
                    success = false, 
                    error = ex.Message 
                });
            }
        }
        
        // This endpoint tests the Redis connection
        [HttpGet("redis-test")]
        public async Task<IActionResult> TestRedis()
        {
            try
            {
                _logger.LogInformation("Testing Redis connection");
                
                bool isConnected = await _redisService.TestConnectionAsync();
                
                if (isConnected)
                {
                    _logger.LogInformation("Redis connection successful");
                    return Ok(new { 
                        success = true, 
                        message = "Redis connection successful"
                    });
                }
                else
                {
                    _logger.LogWarning("Redis connection failed, using in-memory fallback");
                    return Ok(new { 
                        success = true, 
                        message = "Redis connection not available, using in-memory fallback instead"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis connection test failed");
                return StatusCode(500, new { 
                    success = false, 
                    error = $"Redis connection test error: {ex.Message}",
                    stackTrace = ex.StackTrace
                });
            }
        }
    }
}