// Program.cs
using JakeScerriPFTC_Assignment.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

// Force Production mode
Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Production");

// First, create the builder
var builder = WebApplication.CreateBuilder(args);

// Set Google Cloud credentials path
Environment.SetEnvironmentVariable(
    "GOOGLE_APPLICATION_CREDENTIALS",
    builder.Configuration["GoogleCloud:CredentialsPath"] ?? @"pftc-jake_key.json");

// Add services to the container.
builder.Services.AddControllersWithViews();

// Register Google Cloud services
builder.Services.AddSingleton<StorageService>();
builder.Services.AddSingleton<FirestoreService>();
builder.Services.AddSingleton<PubSubService>();
builder.Services.AddSingleton<SecretManagerService>();
builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton<TicketProcessorService>();
builder.Services.AddLogging();

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
    app.UseHsts();
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

app.Run();