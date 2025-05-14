// Services/SecretManagerService.cs
using Google.Cloud.SecretManager.V1;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;

namespace JakeScerriPFTC_Assignment.Services
{
    public class SecretManagerService
    {
        private readonly string _projectId;
        private readonly SecretManagerServiceClient _client;

        public SecretManagerService(IConfiguration configuration)
        {
            _projectId = configuration["GoogleCloud:ProjectId"];
            _client = SecretManagerServiceClient.Create();
            Console.WriteLine($"Initializing SecretManagerService for project: {_projectId}");
        }

        public async Task<string> GetSecretAsync(string secretId, string version = "latest")
        {
            try
            {
                SecretVersionName secretVersionName = new SecretVersionName(_projectId, secretId, version);
                AccessSecretVersionResponse response = await _client.AccessSecretVersionAsync(secretVersionName);
                
                string secretValue = response.Payload.Data.ToStringUtf8();
                Console.WriteLine($"Successfully retrieved secret: {secretId}");
                
                return secretValue;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving secret {secretId}: {ex.Message}");
                throw;
            }
        }
    }
}