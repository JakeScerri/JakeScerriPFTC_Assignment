// Services/SecretManagerService.cs
using Google.Cloud.SecretManager.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace JakeScerriPFTC_Assignment.Services
{
    public class SecretManagerService
    {
        private readonly string _projectId;
        private readonly SecretManagerServiceClient _client;
        private readonly ILogger<SecretManagerService> _logger;

        public SecretManagerService(IConfiguration configuration, ILogger<SecretManagerService> logger)
        {
            _projectId = configuration["GoogleCloud:ProjectId"];
            _client = SecretManagerServiceClient.Create();
            _logger = logger;
            _logger.LogInformation($"Initializing SecretManagerService for project: {_projectId}");
        }

        public async Task<string> GetSecretAsync(string secretId, string version = "latest")
        {
            try
            {
                _logger.LogInformation($"Attempting to access secret: {secretId}");
                
                // Format the secret name correctly for the API
                SecretVersionName secretVersionName = new SecretVersionName(_projectId, secretId, version);
                
                // Access the secret version
                AccessSecretVersionResponse response = await _client.AccessSecretVersionAsync(secretVersionName);
                
                // Convert the payload to string
                string secretValue = response.Payload.Data.ToStringUtf8();
                
                _logger.LogInformation($"Successfully retrieved secret: {secretId}");
                
                return secretValue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving secret {secretId}");
                
                // Log additional details for troubleshooting
                _logger.LogError($"Project ID: {_projectId}, Secret ID: {secretId}, Version: {version}");
                if (ex.InnerException != null)
                {
                    _logger.LogError($"Inner exception: {ex.InnerException.Message}");
                }
                
                // Return empty string instead of throwing to avoid crashes
                return string.Empty;
            }
        }
    }
}