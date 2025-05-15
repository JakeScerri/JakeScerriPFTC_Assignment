// Program.cs
using JakeScerriPFTC_Assignment.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

// Force Production mode at the very beginning of the application
Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Production");

// Log environment variables to help with debugging
Console.WriteLine($"ASPNETCORE_ENVIRONMENT: {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}");
Console.WriteLine($"DOTNET_ENVIRONMENT: {Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")}");

// Create the builder with explicit environment name
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    EnvironmentName = "Production" // Explicitly set environment name
});

// Log builder environment information
Console.WriteLine($"Builder environment name: {builder.Environment.EnvironmentName}");
Console.WriteLine($"IsDevelopment: {builder.Environment.IsDevelopment()}");
Console.WriteLine($"IsProduction: {builder.Environment.IsProduction()}");

// Set Google Cloud credentials path only for local development
if (builder.Environment.IsDevelopment())
{
    Environment.SetEnvironmentVariable(
        "GOOGLE_APPLICATION_CREDENTIALS",
        builder.Configuration["GoogleCloud:CredentialsPath"] ?? @"pftc-jake_key.json");
    
    Console.WriteLine($"GOOGLE_APPLICATION_CREDENTIALS set to: {Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS")}");
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

// Log application environment information
Console.WriteLine($"Application environment: {app.Environment.EnvironmentName}");
Console.WriteLine($"App IsDevelopment: {app.Environment.IsDevelopment()}");
Console.WriteLine($"App IsProduction: {app.Environment.IsProduction()}");

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    Console.WriteLine("WARNING: Application is running in Development mode even though we tried to force Production");
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Add authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

Console.WriteLine($"Application starting in {app.Environment.EnvironmentName} environment");
app.Run();