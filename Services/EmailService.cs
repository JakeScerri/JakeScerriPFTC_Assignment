// Services/EmailService.cs - Complete SMTP Implementation
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using JakeScerriPFTC_Assignment.Models;

namespace JakeScerriPFTC_Assignment.Services
{
    public class EmailService
    {
        private readonly string _smtpHost;
        private readonly int _smtpPort;
        private readonly string _smtpUsername;
        private readonly string _smtpPassword;
        private readonly string _fromEmail;
        private readonly string _authorizedEmail; // For testing
        private readonly ILogger<EmailService> _logger;
        private readonly FirestoreService _firestoreService;

        public EmailService(
            IConfiguration configuration,
            ILogger<EmailService> logger,
            FirestoreService firestoreService)
        {
            _logger = logger;
            _firestoreService = firestoreService;
            
            // SMTP settings from MailGun
            _smtpHost = "smtp.mailgun.org";
            _smtpPort = 587; // As shown in your screenshot
            
            // Username is postmaster@YOUR_DOMAIN_NAME (your sandbox domain)
            _smtpUsername = "postmaster@sandboxc10a4ceea69649a0a6fcf8b446cff315.mailgun.org"; 
            
            // Get password from configuration
            _smtpPassword = configuration["GoogleCloud:MailGun:SmtpPassword"] ?? "your-mailgun-smtp-password";
            
            // From email and authorized recipient
            _fromEmail = "support@sandboxc10a4ceea69649a0a6fcf8b446cff315.mailgun.org";
            _authorizedEmail = "jakescerri.3@gmail.com"; // Your authorized email for testing
            
            _logger.LogInformation($"EmailService initialized with SMTP: Host={_smtpHost}, Port={_smtpPort}, Username={_smtpUsername}");
        }

        public async Task SendTicketNotificationAsync(Ticket ticket)
        {
            try
            {
                _logger.LogInformation($"Preparing to send email notification for ticket: {ticket.Id}");
                
                // Get all technicians - for production system
                var technicians = await _firestoreService.GetTechniciansAsync();
                
                if (technicians == null || technicians.Count == 0)
                {
                    _logger.LogWarning("No technicians found in database. Using authorized email only for testing.");
                }
                
                // For sandbox testing, we'll send to the authorized email
                var recipients = new List<string> { _authorizedEmail };
                
                // Create the message
                string priorityText = ticket.Priority.ToString().ToUpper();
                string subject = $"[{priorityText}] New Ticket: {ticket.Title}";
                
                string messageBody = $@"
                    <html>
                    <body>
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
                
                messageBody += @"
                        <p>Please log in to the ticket system to handle this request.</p>
                    </body>
                    </html>
                ";
                
                // Send email to each recipient
                foreach (var recipient in recipients)
                {
                    try
                    {
                        await SendEmailAsync(_fromEmail, recipient, subject, messageBody);
                        
                        // Log email sent with ticket ID as correlation key (SE4.6.e)
                        _logger.LogInformation(
                            "Email notification for Ticket {TicketId} sent to {Recipient} at {Timestamp}", 
                            ticket.Id, 
                            recipient, 
                            DateTime.UtcNow);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, 
                            "Failed to send email to {Recipient} for Ticket {TicketId}: {ErrorMessage}", 
                            recipient, 
                            ticket.Id,
                            ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending ticket notification emails for Ticket {TicketId}: {ErrorMessage}", 
                    ticket.Id, ex.Message);
                throw;
            }
        }
        
        private async Task SendEmailAsync(string from, string to, string subject, string htmlBody)
        {
            try
            {
                _logger.LogInformation($"Setting up SMTP client: Host={_smtpHost}, Port={_smtpPort}, From={from}, To={to}");
                
                // Create the message
                var mailMessage = new MailMessage
                {
                    From = new MailAddress(from),
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true
                };
                
                mailMessage.To.Add(to);
                
                // Create the SMTP client
                using (var smtpClient = new SmtpClient(_smtpHost, _smtpPort))
                {
                    // Set credentials
                    smtpClient.Credentials = new NetworkCredential(_smtpUsername, _smtpPassword);
                    smtpClient.EnableSsl = true; // Use TLS
                    
                    _logger.LogInformation($"Sending email via SMTP: From={from}, To={to}, Subject=\"{subject}\"");
                    
                    // Send the message asynchronously
                    await smtpClient.SendMailAsync(mailMessage);
                    
                    _logger.LogInformation("Email sent successfully via SMTP");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email via SMTP: {ErrorMessage}", ex.Message);
                throw;
            }
        }
    }
}