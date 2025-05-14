// Services/PubSubService.cs
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using JakeScerriPFTC_Assignment.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace JakeScerriPFTC_Assignment.Services
{
    public class PubSubService
    {
        private readonly string _projectId;
        private readonly string _topicName;
        private readonly PublisherClient _publisherClient;
        private readonly ILogger<PubSubService> _logger;
        private static bool _ticketCreated = false; // Static flag to track if we've created a ticket

        public PubSubService(IConfiguration configuration, ILogger<PubSubService> logger)
        {
            _logger = logger;
            _projectId = configuration["GoogleCloud:ProjectId"];
            _topicName = configuration["GoogleCloud:TopicName"] ?? "tickets-topic-jakescerri";
            
            _logger.LogInformation($"Initializing PubSubService with project: {_projectId}, topic: {_topicName}");
            
            // Create the topic name
            var topicName = new TopicName(_projectId, _topicName);
            
            // Create the publisher client
            _publisherClient = PublisherClient.Create(topicName);
        }

        // AA2.1.a & AA2.1.b - Publish ticket to PubSub with priority attribute
        public async Task<string> PublishTicketAsync(Ticket ticket)
        {
            try
            {
                _logger.LogInformation($"Publishing ticket {ticket.Id} with priority {ticket.Priority}");
                
                // Serialize the ticket to JSON
                var ticketJson = JsonConvert.SerializeObject(ticket);
                
                // Create the message with priority attribute
                var message = new PubsubMessage
                {
                    Data = ByteString.CopyFromUtf8(ticketJson),
                    // Set priority as an attribute for filtering (AA2.1.b)
                    Attributes = 
                    {
                        ["priority"] = ticket.Priority.ToString().ToLower()
                    }
                };

                // Publish the message
                string messageId = await _publisherClient.PublishAsync(message);
                _logger.LogInformation($"Ticket published with ID: {messageId}, Priority: {ticket.Priority}");
                return messageId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error publishing ticket: {ex.Message}");
                throw;
            }
        }
        
        // KU4.1 - Get a ticket from PubSub for processing
       // In PubSubService.cs
public async Task<Ticket> GetNextTicketAsync()
{
    try
    {
        _logger.LogInformation("Getting next ticket from PubSub");
        
        // Create the subscription name
        var subscriptionName = SubscriptionName.FromProjectSubscription(
            _projectId, 
            "tickets-topic-jakescerri-sub" // Use the exact subscription ID you created
        );
        
        try
        {
            // Create a subscriber client
            var subscriberClient = await SubscriberServiceApiClient.CreateAsync();
            
            // Pull a message from the subscription
            var pullResponse = await subscriberClient.PullAsync(new PullRequest
            {
                MaxMessages = 1,
                Subscription = subscriptionName.ToString()
            });
            
            // Check if we got any messages
            if (pullResponse.ReceivedMessages.Count > 0)
            {
                var receivedMessage = pullResponse.ReceivedMessages[0];
                var message = receivedMessage.Message;
                
                // Parse the ticket data from the message
                var ticketJson = message.Data.ToStringUtf8();
                _logger.LogInformation($"Retrieved message from PubSub: {ticketJson}");
                
                var ticket = JsonConvert.DeserializeObject<Ticket>(ticketJson);
                
                // Acknowledge the message to remove it from the queue
                await subscriberClient.AcknowledgeAsync(new AcknowledgeRequest
                {
                    Subscription = subscriptionName.ToString(),
                    AckIds = { receivedMessage.AckId }
                });
                
                _logger.LogInformation($"Retrieved and acknowledged ticket {ticket.Id} from PubSub");
                return ticket;
            }
            
            _logger.LogInformation("No tickets found in PubSub subscription");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pulling messages from PubSub subscription");
            throw;
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error in GetNextTicketAsync");
        return null;
    }
}
        
        // Add a method to reset the flag for testing purposes
        public void ResetTicketCreationFlag()
        {
            _ticketCreated = false;
            _logger.LogInformation("Reset ticket creation flag - will create one more simulated ticket on next call");
        }

        // SE4.6.a - Get tickets by priority (mock implementation)
        public async Task<List<Ticket>> GetTicketsByPriorityAsync(TicketPriority priority)
        {
            try
            {
                _logger.LogInformation($"Getting tickets with priority: {priority}");
                
                // In a real implementation, we would query PubSub by priority attribute
                // Since this is just for demonstration, return an empty list
                _logger.LogInformation($"No tickets with priority {priority}");
                return new List<Ticket>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting tickets with priority {priority}");
                return new List<Ticket>();
            }
        }
    }
}