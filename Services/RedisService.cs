// Services/RedisService.cs
using System;
using System.Collections.Concurrent;
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
        
        // Thread-safe collections for fallback in-memory storage
        private readonly ConcurrentDictionary<string, string> _inMemoryCache = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, byte> _openTickets = new ConcurrentDictionary<string, byte>();
        private readonly ConcurrentDictionary<string, byte> _closedTickets = new ConcurrentDictionary<string, byte>();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _priorityTickets = 
            new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>();
        
        private readonly object _lock = new object();
        private bool _useRedis = true;

        public RedisService(IConfiguration configuration, ILogger<RedisService> logger)
        {
            _logger = logger;
            _configuration = configuration;
            _instanceName = configuration["Redis:InstanceName"] ?? "TicketSystem:";
            
            // Initialize priority dictionaries
            _priorityTickets["high"] = new ConcurrentDictionary<string, byte>();
            _priorityTickets["medium"] = new ConcurrentDictionary<string, byte>();
            _priorityTickets["low"] = new ConcurrentDictionary<string, byte>();
            
            try
            {
                // Create a lazy connection to Redis
                _redisConnection = new Lazy<ConnectionMultiplexer>(() =>
                {
                    string connectionString = configuration["Redis:ConnectionString"];
                    if (string.IsNullOrEmpty(connectionString))
                    {
                        _logger.LogError("Redis connection string is null or empty");
                        _useRedis = false;
                        return null;
                    }
                    
                    _logger.LogInformation($"Connecting to Redis with connection string: {connectionString}");
                    try
                    {
                        return ConnectionMultiplexer.Connect(connectionString);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to connect to Redis. Using in-memory fallback.");
                        _useRedis = false;
                        return null;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing Redis service. Using in-memory fallback.");
                _useRedis = false;
            }
        }

        // Helper to get the Redis database
        public IDatabase GetDatabase()
        {
            if (!_useRedis)
            {
                _logger.LogWarning("Redis is not available. Using in-memory fallback.");
                return null;
            }
            
            try
            {
                return _redisConnection.Value.GetDatabase();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Redis database. Using in-memory fallback.");
                _useRedis = false;
                return null;
            }
        }

        // Helper to format key with instance name
        public string FormatKey(string key)
        {
            return $"{_instanceName}{key}";
        }

        // Save a ticket to storage (Redis or fallback)
        public async Task SaveTicketAsync(Ticket ticket)
        {
            try
            {
                string serializedTicket = JsonConvert.SerializeObject(ticket);
                string key = FormatKey($"ticket:{ticket.Id}");
                string priorityKey = ticket.Priority.ToString().ToLower();
                
                _logger.LogInformation($"Saving ticket {ticket.Id} to storage");
                
                if (_useRedis)
                {
                    try
                    {
                        var db = GetDatabase();
                        if (db != null)
                        {
                            // Store in Redis with expiration (1 week)
                            TimeSpan expiry = TimeSpan.FromDays(7);
                            await db.StringSetAsync(key, serializedTicket, expiry);
                            
                            // Add to the appropriate set
                            if (ticket.Status == TicketStatus.Open)
                            {
                                await db.SetAddAsync(FormatKey("open-tickets"), ticket.Id);
                            }
                            else if (ticket.Status == TicketStatus.Closed)
                            {
                                await db.SetAddAsync(FormatKey("closed-tickets"), ticket.Id);
                            }
                            
                            // Add to priority set
                            await db.SetAddAsync(FormatKey($"priority:{priorityKey}-tickets"), ticket.Id);
                            
                            _logger.LogInformation($"Ticket {ticket.Id} saved to Redis successfully");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error saving ticket {ticket.Id} to Redis. Using in-memory fallback.");
                        _useRedis = false;
                    }
                }
                
                // Fallback to in-memory storage with thread-safety
                _inMemoryCache[key] = serializedTicket;
                
                // Add to appropriate in-memory sets
                if (ticket.Status == TicketStatus.Open)
                {
                    _openTickets[ticket.Id] = 1;
                }
                else if (ticket.Status == TicketStatus.Closed)
                {
                    _closedTickets[ticket.Id] = 1;
                }
                
                // Add to priority set
                _priorityTickets[priorityKey][ticket.Id] = 1;
                
                _logger.LogInformation($"Ticket {ticket.Id} saved to in-memory storage successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving ticket {ticket.Id}");
                throw;
            }
        }

        // Get a ticket from storage
        public async Task<Ticket> GetTicketAsync(string ticketId)
        {
            try
            {
                string key = FormatKey($"ticket:{ticketId}");
                
                _logger.LogInformation($"Getting ticket {ticketId} from storage");
                
                if (_useRedis)
                {
                    try
                    {
                        var db = GetDatabase();
                        if (db != null)
                        {
                            string redisTicketJson = await db.StringGetAsync(key);
                            
                            if (string.IsNullOrEmpty(redisTicketJson))
                            {
                                _logger.LogInformation($"Ticket {ticketId} not found in Redis");
                                return null;
                            }
                            
                            _logger.LogInformation($"Ticket {ticketId} retrieved from Redis successfully");
                            return JsonConvert.DeserializeObject<Ticket>(redisTicketJson);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error getting ticket {ticketId} from Redis. Using in-memory fallback.");
                        _useRedis = false;
                    }
                }
                
                // Fallback to in-memory storage
                if (_inMemoryCache.TryGetValue(key, out string cacheTicketJson))
                {
                    _logger.LogInformation($"Ticket {ticketId} retrieved from in-memory storage successfully");
                    return JsonConvert.DeserializeObject<Ticket>(cacheTicketJson);
                }
                
                _logger.LogInformation($"Ticket {ticketId} not found in storage");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting ticket {ticketId}");
                throw;
            }
        }

        // Get all open tickets
        public async Task<List<Ticket>> GetOpenTicketsAsync()
        {
            try
            {
                _logger.LogInformation("Getting all open tickets from storage");
                
                var tickets = new List<Ticket>();
                
                if (_useRedis)
                {
                    try
                    {
                        var db = GetDatabase();
                        if (db != null)
                        {
                            // Get all open ticket IDs
                            var redisTicketIds = await db.SetMembersAsync(FormatKey("open-tickets"));
                            
                            // Retrieve each ticket
                            foreach (var idValue in redisTicketIds)
                            {
                                var ticket = await GetTicketAsync(idValue.ToString());
                                if (ticket != null)
                                {
                                    tickets.Add(ticket);
                                }
                            }
                            
                            _logger.LogInformation($"Retrieved {tickets.Count} open tickets from Redis");
                            return tickets;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error getting open tickets from Redis. Using in-memory fallback.");
                        _useRedis = false;
                    }
                }
                
                // Fallback to in-memory storage
                foreach (var memoryTicketId in _openTickets.Keys)
                {
                    var ticket = await GetTicketAsync(memoryTicketId);
                    if (ticket != null)
                    {
                        tickets.Add(ticket);
                    }
                }
                
                _logger.LogInformation($"Retrieved {tickets.Count} open tickets from in-memory storage");
                return tickets;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting open tickets");
                throw;
            }
        }

        // Get tickets by priority
        public async Task<List<Ticket>> GetTicketsByPriorityAsync(TicketPriority priority)
        {
            try
            {
                string priorityString = priority.ToString().ToLower();
                _logger.LogInformation($"Getting {priorityString} priority tickets from storage");
                
                var tickets = new List<Ticket>();
                
                if (_useRedis)
                {
                    try
                    {
                        var db = GetDatabase();
                        if (db != null)
                        {
                            // Get ticket IDs for the specified priority
                            string prioritySetKey = FormatKey($"priority:{priorityString}-tickets");
                            var redisTicketIds = await db.SetMembersAsync(prioritySetKey);
                            
                            // Retrieve each ticket
                            foreach (var idValue in redisTicketIds)
                            {
                                var ticket = await GetTicketAsync(idValue.ToString());
                                if (ticket != null)
                                {
                                    tickets.Add(ticket);
                                }
                            }
                            
                            _logger.LogInformation($"Retrieved {tickets.Count} {priorityString} priority tickets from Redis");
                            return tickets;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error getting {priority} priority tickets from Redis. Using in-memory fallback.");
                        _useRedis = false;
                    }
                }
                
                // Fallback to in-memory storage
                if (_priorityTickets.TryGetValue(priorityString, out var memoryTicketIds))
                {
                    foreach (var memoryTicketId in memoryTicketIds.Keys)
                    {
                        var ticket = await GetTicketAsync(memoryTicketId);
                        if (ticket != null)
                        {
                            tickets.Add(ticket);
                        }
                    }
                }
                
                _logger.LogInformation($"Retrieved {tickets.Count} {priorityString} priority tickets from in-memory storage");
                return tickets;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting {priority} priority tickets");
                throw;
            }
        }

        // Close a ticket
        public async Task CloseTicketAsync(string ticketId, string technicianEmail)
        {
            try
            {
                _logger.LogInformation($"Closing ticket {ticketId} in storage");
                
                // Get the ticket first
                var ticket = await GetTicketAsync(ticketId);
                if (ticket == null)
                {
                    _logger.LogWarning($"Ticket {ticketId} not found in storage, cannot close");
                    return;
                }
                
                // Update ticket status
                ticket.Status = TicketStatus.Closed;
                
                // Save updated ticket
                await SaveTicketAsync(ticket);
                
                if (_useRedis)
                {
                    try
                    {
                        var db = GetDatabase();
                        if (db != null)
                        {
                            // Remove from open tickets
                            await db.SetRemoveAsync(FormatKey("open-tickets"), ticketId);
                            
                            // Add to closed tickets
                            await db.SetAddAsync(FormatKey("closed-tickets"), ticketId);
                            
                            _logger.LogInformation($"Ticket {ticketId} closed in Redis successfully");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error closing ticket {ticketId} in Redis. Using in-memory fallback.");
                        _useRedis = false;
                    }
                }
                
                // Fallback to in-memory storage - thread-safe version
                _openTickets.TryRemove(ticketId, out _);
                _closedTickets[ticketId] = 1;
                
                _logger.LogInformation($"Ticket {ticketId} closed in in-memory storage successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error closing ticket {ticketId}");
                throw;
            }
        }

        // Remove old closed tickets
        public async Task<List<Ticket>> RemoveOldClosedTicketsAsync()
        {
            try
            {
                _logger.LogInformation("Removing old closed tickets from storage");
                
                var ticketsToArchive = new List<Ticket>();
                
                if (_useRedis)
                {
                    try
                    {
                        var db = GetDatabase();
                        if (db != null)
                        {
                            // Get all closed ticket IDs
                            var redisClosedIds = await db.SetMembersAsync(FormatKey("closed-tickets"));
                            
                            // Check each ticket
                            foreach (var idValue in redisClosedIds)
                            {
                                string redisTicketId = idValue.ToString();
                                var ticket = await GetTicketAsync(redisTicketId);
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
                                        string prioritySetKey = FormatKey($"priority:{ticket.Priority.ToString().ToLower()}-tickets");
                                        await db.SetRemoveAsync(prioritySetKey, ticket.Id);
                                        
                                        // Remove from closed tickets
                                        await db.SetRemoveAsync(FormatKey("closed-tickets"), ticket.Id);
                                    }
                                }
                            }
                            
                            _logger.LogInformation($"Removed {ticketsToArchive.Count} old closed tickets from Redis");
                            return ticketsToArchive;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error removing old closed tickets from Redis. Using in-memory fallback.");
                        _useRedis = false;
                    }
                }
                
                // Fallback to in-memory storage
                var memoryClosedIdsToRemove = new List<string>();
                
                foreach (var memoryClosedId in _closedTickets.Keys)
                {
                    var ticket = await GetTicketAsync(memoryClosedId);
                    if (ticket != null)
                    {
                        // If ticket is closed and more than a week old
                        if (ticket.Status == TicketStatus.Closed && 
                            (DateTime.UtcNow - ticket.DateUploaded).TotalDays > 7)
                        {
                            ticketsToArchive.Add(ticket);
                            memoryClosedIdsToRemove.Add(memoryClosedId);
                            
                            // Remove from storage
                            string key = FormatKey($"ticket:{ticket.Id}");
                            _inMemoryCache.TryRemove(key, out _);
                            
                            // Remove from priority set
                            string memoryPriorityKey = ticket.Priority.ToString().ToLower();
                            if (_priorityTickets.TryGetValue(memoryPriorityKey, out var prioritySet))
                            {
                                prioritySet.TryRemove(ticket.Id, out _);
                            }
                        }
                    }
                }
                
                // Remove from closed tickets set
                foreach (var idToRemove in memoryClosedIdsToRemove)
                {
                    _closedTickets.TryRemove(idToRemove, out _);
                }
                
                _logger.LogInformation($"Removed {ticketsToArchive.Count} old closed tickets from in-memory storage");
                return ticketsToArchive;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing old closed tickets");
                throw;
            }
        }
        
        // Test if Redis is available
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                if (!_useRedis)
                {
                    _logger.LogWarning("Redis is not available. Using in-memory fallback.");
                    return false;
                }
                
                var db = GetDatabase();
                if (db == null)
                {
                    _logger.LogWarning("Unable to get Redis database. Using in-memory fallback.");
                    _useRedis = false;
                    return false;
                }
                
                string testKey = FormatKey("test-key");
                string testValue = $"Redis test at {DateTime.UtcNow}";
                
                await db.StringSetAsync(testKey, testValue, TimeSpan.FromMinutes(1));
                var retrievedValue = await db.StringGetAsync(testKey);
                
                return !string.IsNullOrEmpty(retrievedValue);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing Redis connection");
                _useRedis = false;
                return false;
            }
        }
    }
}