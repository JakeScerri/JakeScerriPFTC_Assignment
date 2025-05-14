using Google.Cloud.Functions.Framework;
using Google.Cloud.PubSub.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Google.Cloud.SecretManager.V1;
using Google.Cloud.Logging.V2;
using Google.Cloud.Logging.Type; // Added for LogSeverity
using System.Collections.Generic;
using Google.Protobuf.WellKnownTypes;
using StackExchange.Redis;
using JakeScerriPFTC_Assignment.Models;

namespace JakeScerriPFTC_Assignment.CloudFunction
{
    // Add a wrapper class for tickets without modifying the original Ticket model
    public class PubSubTicketWrapper
    {
        public Ticket Ticket { get; set; }
        public string MessageId { get; set; }
    }
    
    public class TicketProcessorFunction : IHttpFunction
    {
        private readonly string _projectId;
        private readonly string _topicName;
        private readonly string _mailgunApiKey;
        private readonly string _mailgunDomain;
        private readonly string _fromEmail;
        private readonly string _redisConnectionString;
        
        public TicketProcessorFunction()
        {
            _projectId = Environment.GetEnvironmentVariable("PROJECT_ID") ?? "pftc-jake";
            _topicName = Environment.GetEnvironmentVariable("TOPIC_NAME") ?? "tickets-topic-jakescerri";
            _mailgunDomain = Environment.GetEnvironmentVariable("MAILGUN_DOMAIN") ?? "sandbox10a4ceea69649a0a6fc8b446cf8115.mailgun.org";
            _fromEmail = Environment.GetEnvironmentVariable("MAILGUN_FROM_EMAIL") ?? "postmaster@sandbox10a4ceea69649a0a6fc8b446cf8115.mailgun.org";
            _redisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
            _mailgunApiKey = ""; // Initialize empty, will be populated in the try-catch below
    
            // Get Mailgun API key from Secret Manager
            try
            {
                var secretClient = SecretManagerServiceClient.Create();
                var secretName = new SecretVersionName(_projectId, "mailgun-api-key", "latest");
                var response = secretClient.AccessSecretVersion(secretName);
                _mailgunApiKey = response.Payload.Data.ToStringUtf8();
                Console.WriteLine("Successfully retrieved Mailgun API key");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving Mailgun API key: {ex.Message}");
            }
        }
        
        public async Task HandleAsync(HttpContext context)
        {
            try
            {
                Console.WriteLine("Starting ticket processing");
                
                // SE4.6.b - Process high priority tickets first
                var highPriorityTickets = await GetTicketsByPriorityAsync("high");
                
                if (highPriorityTickets.Count > 0)
                {
                    Console.WriteLine($"Processing {highPriorityTickets.Count} high priority tickets");
                    foreach (var ticket in highPriorityTickets)
                    {
                        await ProcessTicketAsync(ticket);
                    }
                }
                else
                {
                    // SE4.6.f - If no high priority tickets, process medium priority
                    var mediumPriorityTickets = await GetTicketsByPriorityAsync("medium");
                    
                    if (mediumPriorityTickets.Count > 0)
                    {
                        Console.WriteLine($"Processing {mediumPriorityTickets.Count} medium priority tickets");
                        foreach (var ticket in mediumPriorityTickets)
                        {
                            await ProcessTicketAsync(ticket);
                        }
                    }
                    else
                    {
                        // SE4.6.g - If no medium priority tickets, process low priority
                        var lowPriorityTickets = await GetTicketsByPriorityAsync("low");
                        
                        if (lowPriorityTickets.Count > 0)
                        {
                            Console.WriteLine($"Processing {lowPriorityTickets.Count} low priority tickets");
                            foreach (var ticket in lowPriorityTickets)
                            {
                                await ProcessTicketAsync(ticket);
                            }
                        }
                        else
                        {
                            Console.WriteLine("No tickets to process");
                        }
                    }
                }
                
                await context.Response.WriteAsync("Ticket processing completed successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing tickets: {ex.Message}");
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync($"Error: {ex.Message}");
            }
        }
        
        private async Task<List<PubSubTicketWrapper>> GetTicketsByPriorityAsync(string priority)
        {
            try
            {
                Console.WriteLine($"Getting {priority} priority tickets");
                
                // Create subscriber client
                var subscriberClient = await SubscriberServiceApiClient.CreateAsync();
                
                // Create subscription name
                var subscriptionName = SubscriptionName.FromProjectSubscription(
                    _projectId, 
                    $"{_topicName}-sub" // Your subscription name
                );
                
                // Pull messages
                var pullRequest = new PullRequest
                {
                    MaxMessages = 10,
                    Subscription = subscriptionName.ToString()
                };
                
                var pullResponse = await subscriberClient.PullAsync(pullRequest);
                
                var tickets = new List<PubSubTicketWrapper>();
                
                foreach (var receivedMessage in pullResponse.ReceivedMessages)
                {
                    var message = receivedMessage.Message;
                    
                    // Check message priority attribute matches requested priority
                    if (message.Attributes.TryGetValue("priority", out var messagePriority) && 
                        messagePriority.Equals(priority, StringComparison.OrdinalIgnoreCase))
                    {
                        // Parse the ticket
                        var ticketJson = message.Data.ToStringUtf8();
                        var ticket = JsonConvert.DeserializeObject<Ticket>(ticketJson);
                        
                        // Use the wrapper instead of modifying the Ticket class
                        tickets.Add(new PubSubTicketWrapper
                        {
                            Ticket = ticket,
                            MessageId = receivedMessage.AckId
                        });
                    }
                }
                
                return tickets;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting {priority} priority tickets: {ex.Message}");
                return new List<PubSubTicketWrapper>();
            }
        }
        
        private async Task ProcessTicketAsync(PubSubTicketWrapper ticketWrapper)
        {
            try
            {
                var ticket = ticketWrapper.Ticket;
                Console.WriteLine($"Processing ticket {ticket.Id}");
                
                // SE4.6.c - Save ticket to Redis cache
                await SaveTicketToRedisAsync(ticket);
                
                // SE4.6.d - Send email to technicians
                await SendEmailNotificationAsync(ticket);
                
                // Acknowledge the message
                var subscriberClient = await SubscriberServiceApiClient.CreateAsync();
                var subscriptionName = SubscriptionName.FromProjectSubscription(
                    _projectId, 
                    $"{_topicName}-sub"
                );
                
                await subscriberClient.AcknowledgeAsync(new AcknowledgeRequest
                {
                    Subscription = subscriptionName.ToString(),
                    AckIds = { ticketWrapper.MessageId }
                });
                
                Console.WriteLine($"Ticket {ticket.Id} processed and acknowledged");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing ticket: {ex.Message}");
            }
        }
        
        private async Task SaveTicketToRedisAsync(Ticket ticket)
        {
            try
            {
                Console.WriteLine($"Saving ticket {ticket.Id} to Redis");
                
                // Connect to Redis
                var redis = ConnectionMultiplexer.Connect(_redisConnectionString);
                var db = redis.GetDatabase();
                
                // Serialize ticket to JSON
                var ticketJson = JsonConvert.SerializeObject(ticket);
                
                // Save to Redis with 7-day expiry
                await db.StringSetAsync(
                    $"ticket:{ticket.Id}", 
                    ticketJson, 
                    TimeSpan.FromDays(7)
                );
                
                Console.WriteLine($"Ticket {ticket.Id} saved to Redis");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving ticket to Redis: {ex.Message}");
                throw;
            }
        }
        
        private async Task SendEmailNotificationAsync(Ticket ticket)
        {
            try
            {
                Console.WriteLine($"Sending email notification for ticket {ticket.Id}");
                
                // Set up authentication
                var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"api:{_mailgunApiKey}"));
                
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
                
                // Create email HTML content
                var htmlContent = $@"
                <h2>New Ticket: {ticket.Title}</h2>
                <p><strong>Priority:</strong> {ticket.Priority}</p>
                <p><strong>Reported by:</strong> {ticket.UserEmail}</p>
                <p><strong>Description:</strong></p>
                <p>{ticket.Description}</p>
                <p>Please log in to the system to handle this ticket.</p>
                ";

                // Create email request content
                var formContent = new MultipartFormDataContent
                {
                    { new StringContent($"IT Support System <{_fromEmail}>"), "from" },
                    { new StringContent("jakescerri.3@gmail.com"), "to" }, // Your verified email
                    { new StringContent($"New {ticket.Priority} Priority Ticket: {ticket.Title}"), "subject" },
                    { new StringContent(htmlContent), "html" },
                    { new StringContent(ticket.Id), "h:X-Correlation-ID" }
                };
                
                // Send the email
                var response = await httpClient.PostAsync(
                    $"https://api.mailgun.net/v3/{_mailgunDomain}/messages", 
                    formContent
                );
                
                // SE4.6.e - Log email with correlation ID
                LogEmailOperation(ticket.Id, response.IsSuccessStatusCode, "jakescerri.3@gmail.com");
                
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Email notification sent for ticket {ticket.Id}");
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Failed to send email: {errorContent}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending email: {ex.Message}");
            }
        }
        
        private void LogEmailOperation(string ticketId, bool success, string recipient)
        {
            try
            {
                // Create logging client
                var loggingClient = LoggingServiceV2Client.Create();
                
                // Create log entry with ticket ID as correlation key
                var logName = new LogName(_projectId, "ticket-processor");
                var logEntry = new LogEntry
                {
                    LogName = logName.ToString(),
                    Severity = success ? LogSeverity.Info : LogSeverity.Error,
                    TextPayload = $"Email {(success ? "sent" : "failed")} for ticket {ticketId} to {recipient} at {DateTime.UtcNow}",
                    Labels = { { "correlation_id", ticketId } },
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                };
                
                // Write the log entry
                loggingClient.WriteLogEntries(
                    logName.ToString(),
                    new Google.Api.MonitoredResource { Type = "global" },
                    new Dictionary<string, string>(),
                    new[] { logEntry }
                );
                
                Console.WriteLine($"Logged email operation for ticket {ticketId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error logging email operation: {ex.Message}");
            }
        }
    }
}