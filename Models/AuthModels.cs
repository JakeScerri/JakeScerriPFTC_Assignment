using System;
using System.Collections.Generic;

namespace JakeScerriPFTC_Assignment.Models
{
    public class GoogleAuthConfig
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
    }

    public class GoogleUserData
    {
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Picture { get; set; } = string.Empty;
    }
}