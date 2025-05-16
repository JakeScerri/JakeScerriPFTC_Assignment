// Program.cs
using JakeScerriPFTC_Assignment.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

// Start with extensive diagnostics for Cloud Run
Console.WriteLine("Application starting...");
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
Console.WriteLine($"PORT env var: {port}");
Console.WriteLine($"GOOGLE_APPLICATION_CREDENTIALS: {Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS")}");

// First, create the builder
var builder = WebApplication.CreateBuilder(args);

// Explicitly configure to listen on the right port - VERY IMPORTANT FOR CLOUD RUN
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Log environment information
Console.WriteLine($"Environment: {builder.Environment.EnvironmentName}");
Console.WriteLine($"IsDevelopment: {builder.Environment.IsDevelopment()}");
Console.WriteLine($"IsProduction: {builder.Environment.IsProduction()}");


if (builder.Environment.IsDevelopment())
{
    string credentialsPath = builder.Configuration["GoogleCloud:CredentialsPath"];
    Console.WriteLine($"Setting credentials path for local development: {credentialsPath}");
    
    if (!string.IsNullOrEmpty(credentialsPath))
    {
        Environment.SetEnvironmentVariable(
            "GOOGLE_APPLICATION_CREDENTIALS",
            credentialsPath);
    }
}
else
{
    Console.WriteLine("Running in production environment, using default credentials");
    Console.WriteLine($"Credentials path: {Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS")}");
}

// Add services to the container.
builder.Services.AddControllersWithViews();

// Register Google Cloud services
builder.Services.AddSingleton<StorageService>();
builder.Services.AddSingleton<FirestoreService>();
builder.Services.AddSingleton<PubSubService>();
builder.Services.AddSingleton<SecretManagerService>();
builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton<TicketProcessorService>();

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Add authentication services
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.LoginPath = "/api/auth/login";
        options.LogoutPath = "/api/auth/logout";
    });

// Add authorization services
builder.Services.AddAuthorization();

// Build the app
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // IMPORTANT: Don't use HSTS in Cloud Run
    // app.UseHsts();
}

// IMPORTANT: Skip HTTPS redirection for Cloud Run
// app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseRouting();

// Add authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// Add a global error handler for debugging
app.Use(async (context, next) =>
{
    try
    {
        Console.WriteLine($"Received request: {context.Request.Method} {context.Request.Path}");
        await next();
        
        // If not found and part of api/auth/callback, provide details
        if (context.Response.StatusCode == 404)
        {
            Console.WriteLine($"404 Not Found: {context.Request.Method} {context.Request.Path}");
            
            if (context.Request.Path.Value.Contains("/api/auth/callback"))
            {
                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync(@"
                    <html><body>
                        <h1>Debug - Callback Route Not Found</h1>
                        <p>The callback route was not registered properly.</p>
                        <p>Please check your route configuration in Program.cs</p>
                    </body></html>
                ");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Unhandled exception: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
        
        // Don't rethrow to prevent 500 errors
        context.Response.StatusCode = 500;
        context.Response.ContentType = "text/html";
        await context.Response.WriteAsync($@"
            <html><body>
                <h1>Server Error</h1>
                <p>{ex.Message}</p>
                <pre>{ex.StackTrace}</pre>
            </body></html>
        ");
    }
});

// Add a dedicated endpoint for Cloud Scheduler
app.MapPost("/process-tickets", async (HttpContext context) => 
{
    Console.WriteLine("Process tickets endpoint called");
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var ticketProcessor = context.RequestServices.GetRequiredService<TicketProcessorService>();
    
    logger.LogInformation("Processing tickets from HTTP request at {Time}", DateTime.UtcNow);
    
    try
    {
        var result = await ticketProcessor.ProcessTicketsAsync();
        logger.LogInformation("Ticket processing completed successfully");
        
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new 
        { 
            success = true, 
            message = "Ticket processing completed successfully",
            timestamp = DateTime.UtcNow
        });
    }
    catch (Exception ex) 
    {
        logger.LogError(ex, "Error processing tickets");
        Console.WriteLine($"Error in /process-tickets: {ex.Message}");
        
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new 
        { 
            success = false, 
            error = ex.Message
        });
    }
});

// IMPORTANT: Register MVC routes and attribute-based routing
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
    
// Add this line to enable attribute-based routing (essential for API controllers)
app.MapControllers();

// Health check endpoint for Cloud Run
app.MapGet("/health", () => 
{
    Console.WriteLine($"Health check requested at {DateTime.UtcNow}");
    return Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
});

// Root endpoint for testing
app.MapGet("/", () => 
{
    Console.WriteLine($"Root endpoint requested at {DateTime.UtcNow}");
    return Results.Ok(new { 
        status = "running", 
        message = "JakeScerriPFTC_Assignment API",
        timestamp = DateTime.UtcNow 
    });
});

Console.WriteLine($"Application starting in {app.Environment.EnvironmentName} environment on URL: http://0.0.0.0:{port}");
app.Run();