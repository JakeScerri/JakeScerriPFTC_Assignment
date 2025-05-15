using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using JakeScerriPFTC_Assignment.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth;
using JakeScerriPFTC_Assignment.Services;

namespace JakeScerriPFTC_Assignment.Controllers
{
    [Route("api/[controller]")]
    public class AuthController : Controller
    {
        private readonly GoogleAuthConfig _authConfig;
        private readonly IConfiguration _configuration;
        private readonly FirestoreService _firestoreService;
        private readonly SecretManagerService _secretManagerService;
        private readonly ILogger<AuthController> _logger;
        private readonly IWebHostEnvironment _environment;
        private bool _secretsInitialized = false;

        public AuthController(
            IConfiguration configuration,
            FirestoreService firestoreService,
            SecretManagerService secretManagerService,
            ILogger<AuthController> logger,
            IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _firestoreService = firestoreService;
            _secretManagerService = secretManagerService;
            _logger = logger;
            _environment = environment;
            
            // Initialize with placeholder values - will be filled by InitializeSecretsAsync
            _authConfig = new GoogleAuthConfig
            {
                ClientId = "",
                ClientSecret = "",
                RedirectUri = ""
            };
            
            _logger.LogInformation($"AuthController initialized in {_environment.EnvironmentName} environment");
        }

        private async Task InitializeSecretsAsync()
        {
            if (!_secretsInitialized)
            {
                _logger.LogInformation($"Initializing secrets in {_environment.EnvironmentName} environment");
                
                // Determine if we're running locally (either through explicit env var or looking at server variables)
                bool isRunningLocally = 
                    Environment.GetEnvironmentVariable("RUNNING_LOCALLY") == "true" || 
                    HttpContext.Request.Host.Host.Contains("localhost") ||
                    HttpContext.Request.Host.Host.Equals("127.0.0.1");
                
                _logger.LogInformation($"Is running locally: {isRunningLocally}");
                
                // Set the correct redirect URI based on environment
                if (_environment.IsProduction() && !isRunningLocally)
                {
                    _authConfig.RedirectUri = "https://ticket-system-55855542835.europe-west1.run.app/api/auth/callback";
                    _logger.LogInformation($"Using production redirect URI: {_authConfig.RedirectUri}");
                }
                else
                {
                    // Use localhost with appropriate port
                    string port = HttpContext.Request.Host.Port?.ToString() ?? "8080";
                    _authConfig.RedirectUri = $"http://localhost:{port}/api/auth/callback";
                    _logger.LogInformation($"Using local development redirect URI: {_authConfig.RedirectUri}");
                }

                // Try to get secrets from Secret Manager
                try
                {
                    _logger.LogInformation("Loading OAuth secrets from Secret Manager");
                    
                    // Load secrets from Secret Manager
                    string clientId = await _secretManagerService.GetSecretAsync("oauth-client-id");
                    string clientSecret = await _secretManagerService.GetSecretAsync("oauth-client-secret");
                    
                    // Log if secrets were retrieved (without exposing the values)
                    _logger.LogInformation($"Client ID retrieved: {!string.IsNullOrEmpty(clientId)}");
                    _logger.LogInformation($"Client Secret retrieved: {!string.IsNullOrEmpty(clientSecret)}");
                    
                    // Only use Secret Manager values if they're not empty
                    if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
                    {
                        _authConfig.ClientId = clientId;
                        _authConfig.ClientSecret = clientSecret;
                        _logger.LogInformation("OAuth secrets loaded successfully from Secret Manager");
                    }
                    else
                    {
                        _logger.LogWarning("Secret Manager returned empty values, falling back to configuration");
                        
                        // Fall back to configuration values
                        _authConfig.ClientId = _configuration["GoogleCloud:Auth:ClientId"] ?? "";
                        _authConfig.ClientSecret = _configuration["GoogleCloud:Auth:ClientSecret"] ?? "";
                        
                        if (string.IsNullOrEmpty(_authConfig.ClientId) || string.IsNullOrEmpty(_authConfig.ClientSecret))
                        {
                            throw new Exception("No OAuth credentials available from any source");
                        }
                        
                        _logger.LogWarning("Using fallback OAuth credentials from configuration");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error accessing Secret Manager");
                    
                    // Fall back to configuration values
                    _authConfig.ClientId = _configuration["GoogleCloud:Auth:ClientId"] ?? "";
                    _authConfig.ClientSecret = _configuration["GoogleCloud:Auth:ClientSecret"] ?? "";
                    
                    if (string.IsNullOrEmpty(_authConfig.ClientId) || string.IsNullOrEmpty(_authConfig.ClientSecret))
                    {
                        _logger.LogCritical("No OAuth credentials available from any source");
                        throw new Exception("No OAuth credentials available from any source");
                    }
                    
                    _logger.LogWarning("Using fallback OAuth credentials from configuration");
                }
                
                // Log final configuration (without exposing sensitive values)
                _logger.LogInformation($"Auth configuration complete: ClientId exists={!string.IsNullOrEmpty(_authConfig.ClientId)}, RedirectUri={_authConfig.RedirectUri}");
                _secretsInitialized = true;
            }
        }

        [HttpGet("login")]
        public async Task<IActionResult> Login()
        {
            try
            {
                await InitializeSecretsAsync();
                
                _logger.LogInformation("Creating OAuth authorization URL");
                
                // Create authorization URL
                var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = _authConfig.ClientId,
                        ClientSecret = _authConfig.ClientSecret
                    },
                    Scopes = new[] { "email", "profile" },
                });

                var url = flow.CreateAuthorizationCodeRequest(_authConfig.RedirectUri).Build().ToString();
                _logger.LogInformation($"Redirecting to OAuth login: {url}");
                return Redirect(url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login failed");
                return StatusCode(500, $"Login failed: {ex.Message}");
            }
        }

        [HttpGet("callback")]
        public async Task<IActionResult> Callback([FromQuery] string code)
        {
            try
            {
                _logger.LogInformation("OAuth callback received with code");
                
                await InitializeSecretsAsync();
                
                _logger.LogInformation($"Exchanging code for token with redirect URI: {_authConfig.RedirectUri}");
                
                // Exchange authorization code for tokens
                var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = _authConfig.ClientId,
                        ClientSecret = _authConfig.ClientSecret
                    },
                    Scopes = new[] { "email", "profile" }
                });

                _logger.LogInformation("Exchanging code for token");
                var token = await flow.ExchangeCodeForTokenAsync(
                    "", // Not using a user ID here
                    code,
                    _authConfig.RedirectUri,
                    CancellationToken.None);

                // Validate the token and get user info
                _logger.LogInformation("Validating token");
                var payload = await GoogleJsonWebSignature.ValidateAsync(token.IdToken);
                
                // Get the user's email
                string userEmail = payload.Email;
                _logger.LogInformation($"User authenticated: {userEmail}");
                
                // Check if user exists and get their current role
                var existingUser = await _firestoreService.GetUserByEmailAsync(userEmail);
                UserRole role = UserRole.User; // Default role
                
                // If user exists, preserve their role
                if (existingUser != null)
                {
                    _logger.LogInformation($"User {userEmail} already exists with role {existingUser.Role}");
                    role = existingUser.Role;
                }
                else
                {
                    _logger.LogInformation($"User {userEmail} is new, assigning default User role");
                }
                
                // Save/update user with preserved role
                var user = await _firestoreService.SaveUserAsync(userEmail, role);
                
                // Create claims for authentication
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Email, payload.Email),
                    new Claim(ClaimTypes.Name, payload.Name ?? payload.Email),
                    new Claim("GoogleId", payload.Subject),
                    new Claim("Picture", payload.Picture ?? "")
                };
                
                // Add role claim
                claims.Add(new Claim(ClaimTypes.Role, user.Role.ToString()));
                
                _logger.LogInformation($"User {userEmail} authenticated with role {user.Role}");

                // Create claims identity
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);

                // Sign in the user
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTime.UtcNow.AddDays(7)
                    });

                // Redirect to the home page
                _logger.LogInformation("Authentication successful, redirecting to homepage");
                return Redirect("/");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Authentication failed during callback");
                _logger.LogError($"Exception message: {ex.Message}");
                if (ex.InnerException != null)
                {
                    _logger.LogError($"Inner exception: {ex.InnerException.Message}");
                }
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                
                return StatusCode(500, $"Authentication failed: {ex.Message}");
            }
        }

        [HttpGet("logout")]
        public async Task<IActionResult> Logout()
        {
            _logger.LogInformation("User logging out");
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect("/");
        }

        [HttpGet("user")]
        public IActionResult GetCurrentUser()
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                _logger.LogInformation("User is not authenticated");
                return Json(new { isAuthenticated = false });
            }

            string email = User.FindFirstValue(ClaimTypes.Email);
            _logger.LogInformation($"Returning user info for {email}");
            
            return Json(new
            {
                isAuthenticated = true,
                email = email,
                name = User.FindFirstValue(ClaimTypes.Name),
                picture = User.FindFirstValue("Picture"),
                role = User.FindFirstValue(ClaimTypes.Role)
            });
        }
    }
}