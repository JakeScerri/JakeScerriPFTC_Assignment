// Services/EmailService.cs - Complete implementation
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using JakeScerriPFTC_Assignment.Models;

namespace JakeScerriPFTC_Assignment.Services
{
    public class EmailService
    {
        private readonly string _apiKey;
        private readonly string _domain;
        private readonly string _fromEmail;
        private readonly ILogger<EmailService> _logger;
        private readonly FirestoreService _firestoreService;
        private readonly HttpClient _httpClient;

        public EmailService(
            IConfiguration configuration,
            ILogger<EmailService> logger,
            FirestoreService firestoreService)
        {
            _logger = logger;
            _firestoreService = firestoreService;
            
            // Initialize from configuration
            _apiKey = configuration["GoogleCloud:MailGun:ApiKey"];
            _domain = configuration["GoogleCloud:MailGun:Domain"];
            _fromEmail = configuration["GoogleCloud:MailGun:FromEmail"] ?? "support@example.com";
            
            _logger.LogInformation("EmailService initialized");
            
            // Initialize HttpClient
            _httpClient = new HttpClient();
        }

        public async Task SendTicketNotificationAsync(Ticket ticket)
        {
            try
            {
                _logger.LogInformation($"Preparing to send notification for ticket: {ticket.Id}");
                
                // Get all technicians
                var technicians = await _firestoreService.GetTechniciansAsync();
                
                if (technicians == null || technicians.Count == 0)
                {
                    _logger.LogWarning("No technicians found to send email notifications");
                    return;
                }
                
                // Create the message
                string priorityText = ticket.Priority.ToString().ToUpper();
                string subject = $"[{priorityText}] New Ticket: {ticket.Title}";
                
                string messageBody = $@"
                    <h2>New Ticket Submitted</h2>
                    <p><strong>Ticket ID:</strong> {ticket.Id}</p>
                    <p><strong>Priority:</strong> {priorityText}</p>
                    <p><strong>Submitted By:</strong> {ticket.UserEmail}</p>
                    <p><strong>Title:</strong> {ticket.Title}</p>
                    <p><strong>Description:</strong> {ticket.Description}</p>
                    <p><strong>Date Submitted:</strong> {ticket.DateUploaded}</p>
                ";

                if (ticket.ImageUrls != null && ticket.ImageUrls.Count > 0)
                {
                    messageBody += "<p><strong>Attached Screenshots:</strong></p><ul>";
                    foreach (var imageUrl in ticket.ImageUrls)
                    {
                        messageBody += $"<li><a href='{imageUrl}'>View Screenshot</a></li>";
                    }
                    messageBody += "</ul>";
                }
                
                messageBody += "<p>Please log in to the ticket system to handle this request.</p>";

                // Send emails to all technicians
                foreach (var technician in technicians)
                {
                    try
                    {
                        await SendMailgunEmailAsync(
                            _fromEmail,
                            technician.Email,
                            subject,
                            messageBody
                        );
                        
                        _logger.LogInformation(
                            "Email notification for Ticket {TicketId} sent to {Recipient} at {Timestamp}", 
                            ticket.Id, 
                            technician.Email, 
                            DateTime.UtcNow);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, 
                            "Failed to send email to {TechnicianEmail} for Ticket {TicketId}", 
                            technician.Email, 
                            ticket.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending ticket notification emails for Ticket {TicketId}", ticket.Id);
                throw;
            }
        }
        
        private async Task<string> SendMailgunEmailAsync(string from, string to, string subject, string htmlBody)
        {
            try
            {
                // Set up the HTTP request
                var apiUrl = $"https://api.mailgun.net/v3/{_domain}/messages";
                
                // Set up the HTTP client with Basic Authentication
                var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{_apiKey}"));
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
                
                // Create the form content
                var formContent = new MultipartFormDataContent
                {
                    { new StringContent(from), "from" },
                    { new StringContent(to), "to" },
                    { new StringContent(subject), "subject" },
                    { new StringContent(htmlBody), "html" }
                };
                
                // Send the request
                var response = await _httpClient.PostAsync(apiUrl, formContent);
                
                // Handle the response
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Mailgun API returned status code {response.StatusCode}: {errorContent}");
                }
                
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email through Mailgun API");
                throw;
            }
        }
    }
}