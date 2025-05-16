// Controllers/FunctionController.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JakeScerriPFTC_Assignment.Models;
using JakeScerriPFTC_Assignment.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Google.Cloud.PubSub.V1;
using Newtonsoft.Json;

namespace JakeScerriPFTC_Assignment.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FunctionController : ControllerBase
    {
        private readonly ILogger<FunctionController> _logger;
        private readonly PubSubService _pubSubService;
        private readonly RedisService _redisService;
        private readonly EmailService _emailService;
        private readonly FirestoreService _firestoreService;
        private readonly string _projectId;
        private readonly string _topicName;

        public FunctionController(
            ILogger<FunctionController> logger,
            PubSubService pubSubService,
            RedisService redisService,
            EmailService emailService,
            FirestoreService firestoreService,
            Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _logger = logger;
            _pubSubService = pubSubService;
            _redisService = redisService;
            _emailService = emailService;
            _firestoreService = firestoreService;
            _projectId = configuration["GoogleCloud:ProjectId"];
            _topicName = configuration["GoogleCloud:TopicName"] ?? "tickets-topic-jakescerri";
        }

        // SE4.6.a - HTTP Function that accesses tickets-topic
        [HttpPost("process-tickets")]
        public async Task<IActionResult> ProcessTickets()
        {
            try
            {
                _logger.LogInformation("Starting HTTP Function to process tickets at {Time}", DateTime.UtcNow);
                
                // SE4.6.b - Start by processing high priority tickets
                _logger.LogInformation("Checking for high priority tickets");
                bool processedHighPriority = await ProcessPriorityTicketsAsync(TicketPriority.High);
                
                // SE4.6.f - If no high priority tickets or all are resolved, try medium priority
                if (!processedHighPriority)
                {
                    _logger.LogInformation("No high priority tickets or all are resolved. Checking medium priority tickets");
                    bool processedMediumPriority = await ProcessPriorityTicketsAsync(TicketPriority.Medium);
                    
                    // SE4.6.g - If no medium priority tickets or all are resolved, try low priority
                    if (!processedMediumPriority)
                    {
                        _logger.LogInformation("No medium priority tickets or all are resolved. Checking low priority tickets");
                        await ProcessPriorityTicketsAsync(TicketPriority.Low);
                    }
                }
                
                return Ok(new { 
                    success = true, 
                    message = "Ticket processing completed successfully",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing tickets in HTTP Function");
                return StatusCode(500, new { 
                    success = false, 
                    error = ex.Message 
                });
            }
        }

        // Process tickets of a specific priority
        private async Task<bool> ProcessPriorityTicketsAsync(TicketPriority priority)
        {
            try
            {
                string priorityString = priority.ToString().ToLower();
                _logger.LogInformation($"Processing {priorityString} priority tickets");
                
                // Create subscription name based on priority
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
                    // SE4.6.c - Save ticket to Redis cache (KU4.1.b)
                    await _redisService.SaveTicketAsync(ticket);
                    _logger.LogInformation($"Ticket {ticket.Id} saved to Redis cache");
                    
                    // SE4.6.d - Send email notification to technicians
                    try 
                    {
                        await _emailService.SendTicketNotificationAsync(ticket);
                        
                        // SE4.6.e - Log email with ticket ID as correlation key
                        _logger.LogInformation(
                            "Email notification for Ticket {TicketId} sent with {Priority} priority at {Timestamp}",
                            ticket.Id,
                            priorityString,
                            DateTime.UtcNow);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, 
                            "Failed to send email notification for ticket {TicketId}, but continuing processing", 
                            ticket.Id);
                    }
                    
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
        
        // Get tickets from PubSub by priority
        private async Task<List<Ticket>> GetTicketsFromPubSubAsync(TicketPriority priority)
        {
            try
            {
                string priorityString = priority.ToString().ToLower();
                _logger.LogInformation($"Fetching {priorityString} priority tickets from PubSub");
                
                // Create a subscriber client
                var subscriptionId = $"{_topicName}-{priorityString}-sub";
                var subscriptionName = new SubscriptionName(_projectId, subscriptionId);
                
                _logger.LogInformation($"Using subscription: {subscriptionName}");
                
                // Create a subscriber client
                SubscriberServiceApiClient subscriberClient = null;
                try
                {
                    subscriberClient = await SubscriberServiceApiClient.CreateAsync();
                    
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
                        var topicName = new TopicName(_projectId, _topicName);
                        
                        // SE4.6.b - Create the subscription with filter by priority
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
                    
                    // Pull messages
                    var pullRequest = new PullRequest
                    {
                        SubscriptionAsSubscriptionName = subscriptionName,
                        MaxMessages = 10
                    };
                    
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
                            if (ticket.Priority == priority)
                            {
                                tickets.Add(ticket);
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
                    
                    // SE4.6.b - Acknowledge the messages if we processed any
                    if (ackIds.Count > 0)
                    {
                        // Acknowledge messages
                        var ackRequest = new AcknowledgeRequest
                        {
                            SubscriptionAsSubscriptionName = subscriptionName,
                            AckIds = { ackIds }
                        };
                        
                        await subscriberClient.AcknowledgeAsync(ackRequest);
                        _logger.LogInformation($"Acknowledged {ackIds.Count} messages from subscription {subscriptionName}");
                    }
                    
                    return tickets;
                }
                finally
                {
                    // Clean up if client implements IDisposable
                    if (subscriberClient != null && subscriberClient is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching {priority} priority tickets from PubSub");
                throw;
            }
        }
    }
}