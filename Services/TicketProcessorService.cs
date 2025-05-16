// Services/TicketProcessorService.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using JakeScerriPFTC_Assignment.Models;
using Newtonsoft.Json;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;

namespace JakeScerriPFTC_Assignment.Services
{
    public class TicketProcessorService
    {
        private readonly ILogger<TicketProcessorService> _logger;
        private readonly IConfiguration _configuration;
        private readonly RedisService _redisService;
        private readonly EmailService _emailService;
        private readonly FirestoreService _firestoreService;
        private readonly string _projectId;
        private readonly string _topicName;
        
        public TicketProcessorService(
            ILogger<TicketProcessorService> logger,
            IConfiguration configuration,
            RedisService redisService,
            EmailService emailService,
            FirestoreService firestoreService)
        {
            _logger = logger;
            _configuration = configuration;
            _redisService = redisService;
            _emailService = emailService;
            _firestoreService = firestoreService;
            
            _projectId = configuration["GoogleCloud:ProjectId"];
            _topicName = configuration["GoogleCloud:TopicName"] ?? "tickets-topic-jakescerri";
        }

        // This is the HTTP Function handler
        public async Task<object> ProcessTicketsAsync()
        {
            try
            {
                _logger.LogInformation("Starting ticket processing function");
                
                // Process tickets based on priority (High → Medium → Low)
                bool processedHighPriority = await ProcessPriorityTicketsAsync(TicketPriority.High);
                
                // If no high priority tickets or all high priority tickets are resolved,
                // then process medium priority tickets
                if (!processedHighPriority)
                {
                    bool processedMediumPriority = await ProcessPriorityTicketsAsync(TicketPriority.Medium);
                    
                    // If no medium priority tickets or all medium priority tickets are resolved,
                    // then process low priority tickets
                    if (!processedMediumPriority)
                    {
                        await ProcessPriorityTicketsAsync(TicketPriority.Low);
                    }
                }
                
                // Check for old closed tickets to archive
                await ArchiveOldTicketsAsync();
                
                return new { success = true, message = "Ticket processing completed successfully" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing tickets");
                return new { success = false, error = ex.Message };
            }
        }

        private async Task<bool> ProcessPriorityTicketsAsync(TicketPriority priority)
        {
            try
            {
                string priorityString = priority.ToString().ToLower();
                _logger.LogInformation($"Processing {priorityString} priority tickets");
                
                // Create subscription path if needed
                // In a real implementation, you'd pull messages from PubSub using the priority attribute
                var subscriptionName = $"{_topicName}-{priorityString}-sub";
                
                // Get tickets from PubSub with this priority
                var tickets = await GetTicketsFromPubSubAsync(priority);
                
                if (tickets.Count == 0)
                {
                    _logger.LogInformation($"No {priorityString} priority tickets found in PubSub");
                    return false;
                }
                
                bool processedTickets = false;
                
                // Process each ticket
                foreach (var ticket in tickets)
                {
                    // Save ticket to Redis cache (KU4.1.b - Write to cache)
                    await _redisService.SaveTicketAsync(ticket);
                    
                    // Send email notification to technicians (SE4.6.d)
                    await _emailService.SendTicketNotificationAsync(ticket);
                    
                    // Log email sent (SE4.6.e)
                    _logger.LogInformation(
                        "Email notification for Ticket {TicketId} sent with {Priority} priority",
                        ticket.Id,
                        priorityString);
                    
                    processedTickets = true;
                }
                
                return processedTickets;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing {priority} priority tickets");
                throw;
            }
        }
        
        // Method to archive old closed tickets
        private async Task ArchiveOldTicketsAsync()
        {
            try
            {
                _logger.LogInformation("Checking for old closed tickets to archive");
                
                // Get old closed tickets from Redis
                var ticketsToArchive = await _redisService.RemoveOldClosedTicketsAsync();
                
                // Archive each ticket to Firestore
                foreach (var ticket in ticketsToArchive)
                {
                    // This would store the technician email in a real implementation
                    // For now we'll use a placeholder
                    string technicianEmail = "system@example.com";
                    
                    await _firestoreService.ArchiveTicketAsync(ticket, technicianEmail);
                    
                    _logger.LogInformation($"Ticket {ticket.Id} archived successfully");
                }
                
                _logger.LogInformation($"Archived {ticketsToArchive.Count} old closed tickets");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error archiving old tickets");
                // Don't throw here - we want the main process to continue even if archiving fails
            }
        }
        
        // This is a placeholder method to simulate getting tickets from PubSub
        // In a real implementation, you would use the PubSub client to pull messages
       // Replace the placeholder GetTicketsFromPubSubAsync method with this implementation
       private async Task<List<Ticket>> GetTicketsFromPubSubAsync(TicketPriority priority)
{
    try
    {
        string priorityString = priority.ToString().ToLower();
        _logger.LogInformation($"Pulling {priorityString} priority tickets from PubSub");
        
        // Create a subscriber client
        string projectId = _configuration["GoogleCloud:ProjectId"];
        string subscriptionId = $"{_topicName}-{priorityString}-sub";
        
        // Create proper SubscriptionName object
        var subscriptionName = new SubscriptionName(projectId, subscriptionId);
        
        _logger.LogInformation($"Using subscription: {subscriptionName}");
        
        // Create a subscriber client
        SubscriberServiceApiClient subscriberClient = await SubscriberServiceApiClient.CreateAsync();
        
        // Make sure the subscription exists (create it if it doesn't)
        try
        {
            // Try to get the subscription
            var subscription = await subscriberClient.GetSubscriptionAsync(
                new GetSubscriptionRequest { Subscription = subscriptionName.ToString() });
            
            _logger.LogInformation($"Found existing subscription: {subscriptionName}");
        }
        catch (Exception ex)
        {
            // Subscription doesn't exist, so create it
            _logger.LogInformation($"Subscription not found, creating: {subscriptionName}");
            
            // Create the topic name
            var topicName = new TopicName(projectId, _topicName);
            
            // Create the subscription with a filter for the priority
            var subscriptionRequest = new Subscription
            {
                SubscriptionName = subscriptionName,
                TopicAsTopicName = topicName,
                Filter = $"attributes.priority = \"{priorityString}\"",
                AckDeadlineSeconds = 60
            };
            
            try
            {
                await subscriberClient.CreateSubscriptionAsync(subscriptionRequest);
                _logger.LogInformation($"Created subscription: {subscriptionName}");
            }
            catch (Exception createEx)
            {
                _logger.LogError(createEx, $"Error creating subscription: {subscriptionName}");
                throw;
            }
        }
        
        // Create a subscriber client to pull messages
        var pullRequest = new PullRequest
        {
            SubscriptionAsSubscriptionName = subscriptionName,
            MaxMessages = 10
        };
        
        // Pull messages
        var pullResponse = await subscriberClient.PullAsync(pullRequest);
        var messages = pullResponse.ReceivedMessages;
        
        _logger.LogInformation($"Pulled {messages.Count} messages from subscription {subscriptionName}");
        
        // Process messages and create tickets
        var tickets = new List<Ticket>();
        var ackIds = new List<string>();
        
        foreach (var message in messages)
        {
            try
            {
                // Get the ticket JSON from the message
                string ticketJson = message.Message.Data.ToStringUtf8();
                
                // Deserialize to a Ticket object
                var ticket = JsonConvert.DeserializeObject<Ticket>(ticketJson);
                
                // Verify this ticket has the correct priority 
                // (Should be guaranteed by subscription filter, but double-check)
                if (ticket.Priority == priority)
                {
                    tickets.Add(ticket);
                    
                    // Add the ack ID to acknowledge later
                    ackIds.Add(message.AckId);
                }
                else
                {
                    _logger.LogWarning($"Ticket {ticket.Id} has priority {ticket.Priority} but was pulled from {priorityString} subscription");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing message: {ex.Message}");
            }
        }
        
        // Acknowledge messages if any were processed
        if (ackIds.Count > 0)
        {
            var ackRequest = new AcknowledgeRequest
            {
                SubscriptionAsSubscriptionName = subscriptionName,
                AckIds = { ackIds }
            };
            
            await subscriberClient.AcknowledgeAsync(ackRequest);
            _logger.LogInformation($"Acknowledged {ackIds.Count} messages");
        }
        
        _logger.LogInformation($"Processed {tickets.Count} {priorityString} tickets from PubSub");
        return tickets;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, $"Error pulling {priority} priority tickets from PubSub");
        throw;
    }
}
    }
}