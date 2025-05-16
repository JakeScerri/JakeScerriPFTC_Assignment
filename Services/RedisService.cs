// Services/RedisService.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JakeScerriPFTC_Assignment.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace JakeScerriPFTC_Assignment.Services
{
    public class RedisService
    {
        private readonly ILogger<RedisService> _logger;
        private readonly IConfiguration _configuration;
        private readonly Lazy<ConnectionMultiplexer> _redisConnection;
        private readonly string _instanceName;

        public RedisService(IConfiguration configuration, ILogger<RedisService> logger)
        {
            _logger = logger;
            _configuration = configuration;
            _instanceName = configuration["Redis:InstanceName"] ?? "TicketSystem:";
            
            // Create a lazy connection to Redis
            _redisConnection = new Lazy<ConnectionMultiplexer>(() =>
            {
                string connectionString = _configuration["Redis:ConnectionString"];
                _logger.LogInformation($"Connecting to Redis with connection string: {connectionString}");
                return ConnectionMultiplexer.Connect(connectionString);
            });
        }

        // Helper to get the Redis database
        private IDatabase GetDatabase()
        {
            return _redisConnection.Value.GetDatabase();
        }

        // Helper to format key with instance name
        private string FormatKey(string key)
        {
            return $"{_instanceName}{key}";
        }

        // Save a ticket to Redis
        public async Task SaveTicketAsync(Ticket ticket)
        {
            try
            {
                _logger.LogInformation($"Saving ticket {ticket.Id} to Redis");
                var db = GetDatabase();
                
                // Serialize the ticket to JSON
                string ticketJson = JsonConvert.SerializeObject(ticket);
                
                // Create a key for the ticket
                string key = FormatKey($"ticket:{ticket.Id}");
                
                // Store in Redis with expiration (1 week)
                TimeSpan expiry = TimeSpan.FromDays(7);
                await db.StringSetAsync(key, ticketJson, expiry);
                
                // Also add to set of all open tickets for easy lookup
                await db.SetAddAsync(FormatKey("open-tickets"), ticket.Id);
                
                // Add to the appropriate priority set
                string priorityKey = FormatKey($"priority:{ticket.Priority.ToString().ToLower()}-tickets");
                await db.SetAddAsync(priorityKey, ticket.Id);
                
                _logger.LogInformation($"Ticket {ticket.Id} saved to Redis successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving ticket {ticket.Id} to Redis");
                throw;
            }
        }

        // Get a ticket from Redis
        public async Task<Ticket> GetTicketAsync(string ticketId)
        {
            try
            {
                _logger.LogInformation($"Getting ticket {ticketId} from Redis");
                var db = GetDatabase();
                
                string key = FormatKey($"ticket:{ticketId}");
                string ticketJson = await db.StringGetAsync(key);
                
                if (string.IsNullOrEmpty(ticketJson))
                {
                    _logger.LogInformation($"Ticket {ticketId} not found in Redis");
                    return null;
                }
                
                _logger.LogInformation($"Ticket {ticketId} retrieved from Redis successfully");
                return JsonConvert.DeserializeObject<Ticket>(ticketJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting ticket {ticketId} from Redis");
                throw;
            }
        }

        // Get all open tickets from Redis
        public async Task<List<Ticket>> GetOpenTicketsAsync()
        {
            try
            {
                _logger.LogInformation("Getting all open tickets from Redis");
                var db = GetDatabase();
                
                // Get all open ticket IDs
                var ticketIds = await db.SetMembersAsync(FormatKey("open-tickets"));
                
                // Retrieve each ticket
                var tickets = new List<Ticket>();
                foreach (var id in ticketIds)
                {
                    var ticket = await GetTicketAsync(id.ToString());
                    if (ticket != null)
                    {
                        tickets.Add(ticket);
                    }
                }
                
                _logger.LogInformation($"Retrieved {tickets.Count} open tickets from Redis");
                return tickets;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting open tickets from Redis");
                throw;
            }
        }

        // Get tickets by priority
        public async Task<List<Ticket>> GetTicketsByPriorityAsync(TicketPriority priority)
        {
            try
            {
                string priorityString = priority.ToString().ToLower();
                _logger.LogInformation($"Getting {priorityString} priority tickets from Redis");
                var db = GetDatabase();
                
                // Get ticket IDs for the specified priority
                string priorityKey = FormatKey($"priority:{priorityString}-tickets");
                var ticketIds = await db.SetMembersAsync(priorityKey);
                
                // Retrieve each ticket
                var tickets = new List<Ticket>();
                foreach (var id in ticketIds)
                {
                    var ticket = await GetTicketAsync(id.ToString());
                    if (ticket != null)
                    {
                        tickets.Add(ticket);
                    }
                }
                
                _logger.LogInformation($"Retrieved {tickets.Count} {priorityString} priority tickets from Redis");
                return tickets;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting {priority} priority tickets from Redis");
                throw;
            }
        }

        // Close a ticket and update it in Redis
        public async Task CloseTicketAsync(string ticketId, string technicianEmail)
        {
            try
            {
                _logger.LogInformation($"Closing ticket {ticketId} in Redis");
                
                // Get the ticket first
                var ticket = await GetTicketAsync(ticketId);
                if (ticket == null)
                {
                    _logger.LogWarning($"Ticket {ticketId} not found in Redis, cannot close");
                    return;
                }
                
                // Update ticket status
                ticket.Status = TicketStatus.Closed;
                
                // Save updated ticket
                await SaveTicketAsync(ticket);
                
                // Remove from open tickets
                var db = GetDatabase();
                await db.SetRemoveAsync(FormatKey("open-tickets"), ticketId);
                
                // Add to closed tickets
                await db.SetAddAsync(FormatKey("closed-tickets"), ticketId);
                
                _logger.LogInformation($"Ticket {ticketId} closed in Redis successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error closing ticket {ticketId} in Redis");
                throw;
            }
        }

        // Remove old closed tickets (more than 1 week old)
        public async Task<List<Ticket>> RemoveOldClosedTicketsAsync()
        {
            try
            {
                _logger.LogInformation("Removing old closed tickets from Redis");
                var db = GetDatabase();
                
                // Get all closed ticket IDs
                var closedTicketIds = await db.SetMembersAsync(FormatKey("closed-tickets"));
                
                // Check each ticket
                var ticketsToArchive = new List<Ticket>();
                foreach (var id in closedTicketIds)
                {
                    var ticket = await GetTicketAsync(id.ToString());
                    if (ticket != null)
                    {
                        // If ticket is closed and more than a week old
                        if (ticket.Status == TicketStatus.Closed && 
                            (DateTime.UtcNow - ticket.DateUploaded).TotalDays > 7)
                        {
                            ticketsToArchive.Add(ticket);
                            
                            // Remove from Redis
                            string key = FormatKey($"ticket:{ticket.Id}");
                            await db.KeyDeleteAsync(key);
                            
                            // Remove from priority set
                            string priorityKey = FormatKey($"priority:{ticket.Priority.ToString().ToLower()}-tickets");
                            await db.SetRemoveAsync(priorityKey, ticket.Id);
                            
                            // Remove from closed tickets
                            await db.SetRemoveAsync(FormatKey("closed-tickets"), ticket.Id);
                        }
                    }
                }
                
                _logger.LogInformation($"Removed {ticketsToArchive.Count} old closed tickets from Redis");
                return ticketsToArchive;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing old closed tickets from Redis");
                throw;
            }
        }
    }
}