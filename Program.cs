// Program.cs
using JakeScerriPFTC_Assignment.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

// First, create the builder
var builder = WebApplication.CreateBuilder(args);

// Set Google Cloud credentials path
Environment.SetEnvironmentVariable(
    "GOOGLE_APPLICATION_CREDENTIALS",
    builder.Configuration["GoogleCloud:CredentialsPath"] ?? @"E:\JakeScerriPFTC_Assignment\pftc-jake_key.json");

// Add services to the container.
builder.Services.AddControllersWithViews();

// Register Google Cloud services
builder.Services.AddSingleton<StorageService>();
builder.Services.AddSingleton<FirestoreService>();
builder.Services.AddSingleton<PubSubService>();
builder.Services.AddSingleton<SecretManagerService>();

builder.Services.AddSingleton<IRedisService, RedisService>();


builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton<TicketProcessorService>();
builder.Services.AddControllersWithViews();

// Add authentication services with enhanced configuration
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
        
        // Add events to validate the role claim
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                // This runs on every request to validate the cookie
                if (context.Principal.Identity.IsAuthenticated)
                {
                    var email = context.Principal.FindFirstValue(ClaimTypes.Email);
                    if (!string.IsNullOrEmpty(email))
                    {
                        // Get the FirestoreService
                        var firestoreService = context.HttpContext.RequestServices
                            .GetRequiredService<FirestoreService>();
                            
                        // Get the user from Firestore to ensure we have the latest role
                        var user = await firestoreService.GetUserByEmailAsync(email);
                        
                        if (user != null)
                        {
                            var roleClaim = context.Principal.FindFirst(ClaimTypes.Role);
                            
                            // If role from claims doesn't match role in Firestore, update the claim
                            if (roleClaim == null || roleClaim.Value != user.Role.ToString())
                            {
                                // Create a new identity with the correct role
                                var claims = new List<Claim>(context.Principal.Claims);
                                
                                // Remove existing role claim
                                if (roleClaim != null)
                                {
                                    claims.Remove(roleClaim);
                                }
                                
                                // Add the correct role claim
                                claims.Add(new Claim(ClaimTypes.Role, user.Role.ToString()));
                                
                                // Create new ClaimsIdentity with the updated claims
                                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                                var principal = new ClaimsPrincipal(identity);
                                
                                // Update the context principal
                                context.ReplacePrincipal(principal);
                                context.ShouldRenew = true;
                            }
                        }
                    }
                }
            }
        };
    });

// Add authorization services
builder.Services.AddAuthorization();

// Build the app
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// In the app configuration section, add the middleware (after UseRouting)
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();