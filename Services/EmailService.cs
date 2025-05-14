// Services/EmailService.cs
using Google.Cloud.SecretManager.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace JakeScerriPFTC_Assignment.Services
{
    public interface IEmailService
    {
        Task<bool> SendEmailAsync(string to, string subject, string htmlContent, string correlationId = null);
    }

    public class EmailService : IEmailService
    {
        private readonly string _mailgunApiKey;
        private readonly string _mailgunDomain;
        private readonly string _fromEmail;
        private readonly HttpClient _httpClient;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
            
            try
            {
                // Get project ID from configuration
                string projectId = configuration["GoogleCloud:ProjectId"];
                
                // Get Mailgun API key from Secret Manager
                SecretManagerServiceClient secretClient = SecretManagerServiceClient.Create();
                string secretName = $"projects/{projectId}/secrets/mailgun-api-key/versions/latest";
                AccessSecretVersionResponse response = secretClient.AccessSecretVersion(secretName);
                _mailgunApiKey = response.Payload.Data.ToStringUtf8();
                
                // Get other values from configuration
                _mailgunDomain = configuration["Mailgun:Domain"];
                _fromEmail = configuration["Mailgun:FromEmail"] ?? "postmaster@" + _mailgunDomain;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing EmailService");
                throw;
            }
        }

        public async Task<bool> SendEmailAsync(string to, string subject, string htmlContent, string correlationId = null)
        {
            try
            {
                // Set up authentication header
                var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{_mailgunApiKey}"));
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
                
                // Create form content
                var formContent = new MultipartFormDataContent
                {
                    { new StringContent($"IT Support <{_fromEmail}>"), "from" },
                    { new StringContent(to), "to" },
                    { new StringContent(subject), "subject" },
                    { new StringContent(htmlContent), "html" }
                };
                
                // Add correlation ID if provided
                if (!string.IsNullOrEmpty(correlationId))
                {
                    formContent.Add(new StringContent(correlationId), "h:X-Correlation-ID");
                }
                
                // Send the request
                var response = await _httpClient.PostAsync(
                    $"https://api.mailgun.net/v3/{_mailgunDomain}/messages", 
                    formContent
                );
                
                // Check if successful
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"Email sent successfully to {to}");
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Failed to send email: {response.StatusCode} - {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending email to {to}");
                return false;
            }
        }
    }
}