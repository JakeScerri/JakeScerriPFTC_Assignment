// Services/TicketProcessorService.cs
using JakeScerriPFTC_Assignment.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace JakeScerriPFTC_Assignment.Services
{
    public class TicketProcessorService
    {
        private readonly PubSubService _pubSubService;
        private readonly IRedisService _redisService;
        private readonly FirestoreService _firestoreService;
        private readonly ILogger<TicketProcessorService> _logger;

        public TicketProcessorService(
            PubSubService pubSubService,
            IRedisService redisService,
            FirestoreService firestoreService,
            ILogger<TicketProcessorService> logger)
        {
            _pubSubService = pubSubService;
            _redisService = redisService;
            _firestoreService = firestoreService;
            _logger = logger;
        }

        public async Task ProcessTicketsAsync()
        {
            try
            {
                _logger.LogInformation("Processing new tickets");
                
                // Get a ticket from PubSub topic
                var ticket = await _pubSubService.GetNextTicketAsync();
                
                if (ticket != null)
                {
                    // KU4.1.b - Save to Redis cache
                    await _redisService.SaveTicketAsync(ticket);
                    
                    _logger.LogInformation($"Ticket {ticket.Id} processed and saved to Redis");
                }
                else
                {
                    _logger.LogInformation("No tickets to process");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing tickets");
                throw;
            }
        }
        
        // Add a method to manually process a specific ticket (for testing)
        public async Task ProcessSpecificTicketAsync(Ticket ticket)
        {
            try
            {
                if (ticket != null)
                {
                    _logger.LogInformation($"Processing specific ticket {ticket.Id}");
                    
                    // Save to Redis cache
                    await _redisService.SaveTicketAsync(ticket);
                    
                    _logger.LogInformation($"Ticket {ticket.Id} processed and saved to Redis");
                }
                else
                {
                    _logger.LogInformation("No ticket provided to process");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing specific ticket");
                throw;
            }
        }
    }
}