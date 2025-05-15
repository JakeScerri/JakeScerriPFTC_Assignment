// Services/TicketProcessorService.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using JakeScerriPFTC_Assignment.Models;
using Newtonsoft.Json;

namespace JakeScerriPFTC_Assignment.Services
{
    public class TicketProcessorService
    {
        private readonly ILogger<TicketProcessorService> _logger;
        private readonly PubSubService _pubSubService;
        private readonly EmailService _emailService;
        private readonly FirestoreService _firestoreService;
        
        // In a real application, this would be a Redis cache
        // For this assignment, we'll use an in-memory dictionary
        private readonly Dictionary<string, Ticket> _ticketCache = new Dictionary<string, Ticket>();

        public TicketProcessorService(
            ILogger<TicketProcessorService> logger,
            PubSubService pubSubService,
            EmailService emailService,
            FirestoreService firestoreService)
        {
            _logger = logger;
            _pubSubService = pubSubService;
            _emailService = emailService;
            _firestoreService = firestoreService;
        }

        // This is the HTTP Function handler
        public async Task<object> ProcessTicketsAsync()
        {
            try
            {
                _logger.LogInformation("Starting ticket processing function");
                
                // Process tickets based on priority (High → Medium → Low)
                await ProcessPriorityTicketsAsync("high");
                
                // If no high priority tickets or all high priority tickets are resolved,
                // then process medium priority tickets
                await ProcessPriorityTicketsAsync("medium");
                
                // If no medium priority tickets or all medium priority tickets are resolved,
                // then process low priority tickets
                await ProcessPriorityTicketsAsync("low");
                
                return new { success = true, message = "Ticket processing completed successfully" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing tickets");
                return new { success = false, error = ex.Message };
            }
        }

        private async Task ProcessPriorityTicketsAsync(string priority)
        {
            _logger.LogInformation($"Processing {priority} priority tickets");
            
            // TODO: In a real implementation, you would pull messages from PubSub
            // For this assignment, we'll simulate it
            
            // Placeholder for testing
            var ticket = new Ticket
            {
                Id = Guid.NewGuid().ToString(),
                Title = $"Test {priority} priority ticket",
                Description = "This is a test ticket for demonstration purposes",
                UserEmail = "testuser@example.com",
                Priority = GetPriorityFromString(priority),
                DateUploaded = DateTime.UtcNow,
                Status = TicketStatus.Open
            };
            
            // Save ticket to cache (KU4.1.b - Write to cache)
            _ticketCache[ticket.Id] = ticket;
            
            // Send email notification to technicians (SE4.6.d)
            await _emailService.SendTicketNotificationAsync(ticket);
            
            // Log email sent (SE4.6.e)
            _logger.LogInformation(
                "Email notification for Ticket {TicketId} sent with {Priority} priority",
                ticket.Id,
                priority);
        }
        
        // Helper method to convert string priority to enum
        private TicketPriority GetPriorityFromString(string priority)
        {
            return priority.ToLower() switch
            {
                "high" => TicketPriority.High,
                "medium" => TicketPriority.Medium,
                "low" => TicketPriority.Low,
                _ => TicketPriority.Low
            };
        }
    }
}