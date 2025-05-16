// Services/PubSubService.cs
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using JakeScerriPFTC_Assignment.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace JakeScerriPFTC_Assignment.Services
{
    public class PubSubService
    {
        private readonly PublisherClient _publisherClient;
        private readonly string _projectId;
        private readonly string _topicName;
        private readonly ILogger<PubSubService> _logger;
        
        // Store message IDs with ticket IDs for acknowledgment
        private readonly Dictionary<string, Dictionary<string, string>> _messageAckIds = new Dictionary<string, Dictionary<string, string>>();

        public PubSubService(IConfiguration configuration, ILogger<PubSubService> logger)
        {
            _projectId = configuration["GoogleCloud:ProjectId"];
            _topicName = configuration["GoogleCloud:TopicName"] ?? "tickets-topic-jakescerri";
            _logger = logger;
            
            var topicName = new TopicName(_projectId, _topicName);
            _publisherClient = PublisherClient.Create(topicName);
            
            // Initialize dictionaries for each priority
            _messageAckIds["high"] = new Dictionary<string, string>();
            _messageAckIds["medium"] = new Dictionary<string, string>();
            _messageAckIds["low"] = new Dictionary<string, string>();
            
            _logger.LogInformation($"PubSubService initialized with project: {_projectId}, topic: {_topicName}");
        }

        public async Task<string> PublishTicketAsync(Ticket ticket)
        {
            try
            {
                _logger.LogInformation($"Publishing ticket {ticket.Id} with priority {ticket.Priority}");
                
                // Serialize the ticket to JSON
                var ticketJson = JsonConvert.SerializeObject(ticket);
                var message = new PubsubMessage
                {
                    Data = ByteString.CopyFromUtf8(ticketJson),
                    // Set priority as an attribute for filtering (AA2.1.b)
                    Attributes = 
                    {
                        ["priority"] = ticket.Priority.ToString().ToLower(),
                        ["ticketId"] = ticket.Id // Add ticket ID as attribute for easier lookup
                    }
                };

                // Publish the message
                string messageId = await _publisherClient.PublishAsync(message);
                
                // Store the message ID for this ticket
                string priorityKey = ticket.Priority.ToString().ToLower();
                if (_messageAckIds.ContainsKey(priorityKey))
                {
                    _messageAckIds[priorityKey][ticket.Id] = messageId;
                }
                
                _logger.LogInformation($"Ticket {ticket.Id} published with message ID: {messageId}, Priority: {ticket.Priority}");
                return messageId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error publishing ticket {ticket.Id}: {ex.Message}");
                throw;
            }
        }
        
        // Method to register a message ID for a ticket
        public void RegisterMessageId(string ticketId, TicketPriority priority, string messageId, string ackId)
        {
            try
            {
                string priorityKey = priority.ToString().ToLower();
                
                _logger.LogInformation($"Registering message ID {messageId} and ack ID for ticket {ticketId}");
                
                if (_messageAckIds.ContainsKey(priorityKey))
                {
                    // Store both message ID and ack ID
                    _messageAckIds[priorityKey][ticketId] = ackId;
                    _logger.LogInformation($"Registered ack ID for ticket {ticketId}");
                }
                else
                {
                    _logger.LogWarning($"Unable to register ack ID for ticket {ticketId}: unknown priority {priorityKey}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error registering message ID for ticket {ticketId}: {ex.Message}");
            }
        }
        
        // Method to acknowledge a ticket's message when it's closed
        public async Task AcknowledgeTicketAsync(string ticketId, TicketPriority priority)
{
    try
    {
        string priorityKey = priority.ToString().ToLower();
        _logger.LogInformation($"Acknowledging message for ticket {ticketId} with priority {priorityKey}");
        
        // Check if we have an ack ID for this ticket
        if (_messageAckIds.ContainsKey(priorityKey) && _messageAckIds[priorityKey].ContainsKey(ticketId))
        {
            string ackId = _messageAckIds[priorityKey][ticketId];
            _logger.LogInformation($"Found ack ID {ackId} for ticket {ticketId}");
            
            // Create subscription name
            var subscriptionName = $"{_topicName}-{priorityKey}-sub";
            var subscriptionPath = new SubscriptionName(_projectId, subscriptionName);
            
            // Create subscriber client without using statement
            SubscriberServiceApiClient subscriberClient = null;
            try
            {
                subscriberClient = await SubscriberServiceApiClient.CreateAsync();
                
                // Acknowledge the message
                var ackRequest = new AcknowledgeRequest
                {
                    SubscriptionAsSubscriptionName = subscriptionPath,
                    AckIds = { ackId }
                };
                
                await subscriberClient.AcknowledgeAsync(ackRequest);
                
                // Remove from our tracking dictionary
                _messageAckIds[priorityKey].Remove(ticketId);
                
                _logger.LogInformation($"Successfully acknowledged message for ticket {ticketId}");
            }
            finally
            {
                // Clean up if the client implements IDisposable
                if (subscriberClient != null && subscriberClient is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
        else
        {
            // If we don't have an ack ID, try to find the message by pulling with filter
            _logger.LogWarning($"No ack ID found for ticket {ticketId}, attempting to find by pulling messages");
            
            await FindAndAcknowledgeTicketMessageAsync(ticketId, priority);
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, $"Error acknowledging message for ticket {ticketId}: {ex.Message}");
        throw;
    }
}
        
        // Helper method to find a ticket's message by pulling with filter
       private async Task FindAndAcknowledgeTicketMessageAsync(string ticketId, TicketPriority priority)
{
    try
    {
        string priorityKey = priority.ToString().ToLower();
        _logger.LogInformation($"Searching for ticket {ticketId} in subscription");
        
        // Create subscription name
        var subscriptionName = $"{_topicName}-{priorityKey}-sub";
        var subscriptionPath = new SubscriptionName(_projectId, subscriptionName);
        
        // Create subscriber client without using statement
        SubscriberServiceApiClient subscriberClient = null;
        try
        {
            subscriberClient = await SubscriberServiceApiClient.CreateAsync();
            
            // Pull messages with max size to search through them
            var pullRequest = new PullRequest
            {
                SubscriptionAsSubscriptionName = subscriptionPath,
                MaxMessages = 100 // Adjust as needed
            };
            
            var pullResponse = await subscriberClient.PullAsync(pullRequest);
            
            _logger.LogInformation($"Pulled {pullResponse.ReceivedMessages.Count} messages to search for ticket {ticketId}");
            
            // Search for our ticket
            string ackIdToUse = null;
            
            foreach (var receivedMessage in pullResponse.ReceivedMessages)
            {
                // Check if this message has the ticket ID as an attribute
                if (receivedMessage.Message.Attributes.TryGetValue("ticketId", out string msgTicketId) && 
                    msgTicketId == ticketId)
                {
                    ackIdToUse = receivedMessage.AckId;
                    _logger.LogInformation($"Found message for ticket {ticketId} with ack ID {ackIdToUse}");
                    break;
                }
                
                // If no attribute, try parsing the message body
                try
                {
                    string json = receivedMessage.Message.Data.ToStringUtf8();
                    var msgTicket = JsonConvert.DeserializeObject<Ticket>(json);
                    
                    if (msgTicket != null && msgTicket.Id == ticketId)
                    {
                        ackIdToUse = receivedMessage.AckId;
                        _logger.LogInformation($"Found message for ticket {ticketId} by parsing message data, ack ID {ackIdToUse}");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to parse message as ticket: {ex.Message}");
                }
            }
            
            // If we found the message, acknowledge it
            if (ackIdToUse != null)
            {
                var ackRequest = new AcknowledgeRequest
                {
                    SubscriptionAsSubscriptionName = subscriptionPath,
                    AckIds = { ackIdToUse }
                };
                
                await subscriberClient.AcknowledgeAsync(ackRequest);
                _logger.LogInformation($"Successfully acknowledged message for ticket {ticketId}");
            }
            else
            {
                _logger.LogWarning($"Could not find message for ticket {ticketId} to acknowledge");
            }
        }
        finally
        {
            // If the client implements IDisposable and needs to be cleaned up
            // Wrap this in a try-catch if there's any chance it could throw
            if (subscriberClient != null && subscriberClient is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, $"Error searching for and acknowledging ticket {ticketId}: {ex.Message}");
        throw;
    }
}
    }
}