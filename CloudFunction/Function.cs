using Google.Cloud.Functions.Framework;
using Microsoft.AspNetCore.Http;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace ProcessTicketsFunction
{
    public class Function : IHttpFunction
    {
        public async Task HandleAsync(HttpContext context)
        {
            Console.WriteLine($"Function triggered at {DateTime.UtcNow}");
            
            try
            {
                // Process tickets (simplified for testing)
                Console.WriteLine("Processing high priority tickets");
                Console.WriteLine("Processing medium priority tickets");
                Console.WriteLine("Processing low priority tickets");
                
                // Send a successful response
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                
                var response = new
                {
                    success = true,
                    message = "Ticket processing completed successfully",
                    timestamp = DateTime.UtcNow
                };
                
                await JsonSerializer.SerializeAsync(context.Response.Body, response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                
                var error = new
                {
                    success = false,
                    error = ex.Message
                };
                
                await JsonSerializer.SerializeAsync(context.Response.Body, error);
            }
        }
    }
}