// Services/PubSubService.cs
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using JakeScerriPFTC_Assignment.Models;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace JakeScerriPFTC_Assignment.Services
{
    public class PubSubService
    {
        private readonly PublisherClient _publisherClient;
        private readonly string _projectId;
        private readonly string _topicName;

        public PubSubService(IConfiguration configuration)
        {
            _projectId = configuration["GoogleCloud:ProjectId"];
            _topicName = configuration["GoogleCloud:TopicName"] ?? "tickets-topic-jakescerri";
            
            var topicName = new TopicName(_projectId, _topicName);
            _publisherClient = PublisherClient.Create(topicName);
        }

        public async Task<string> PublishTicketAsync(Ticket ticket)
        {
            try
            {
                // Serialize the ticket to JSON
                var ticketJson = JsonConvert.SerializeObject(ticket);
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
                Console.WriteLine($"Ticket published with ID: {messageId}, Priority: {ticket.Priority}");
                return messageId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error publishing ticket: {ex.Message}");
                throw;
            }
        }
    }
}