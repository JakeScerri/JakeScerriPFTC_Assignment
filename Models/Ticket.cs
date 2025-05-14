// Models/Ticket.cs
using System;
using System.Collections.Generic;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace JakeScerriPFTC_Assignment.Models
{
    public class Ticket
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime DateUploaded { get; set; } = DateTime.UtcNow;
        public string UserEmail { get; set; } = string.Empty;
        public List<string> ImageUrls { get; set; } = new List<string>();
        public TicketPriority Priority { get; set; }
        public TicketStatus Status { get; set; } = TicketStatus.Open;
    }

    public enum TicketPriority
    {
        High,
        Medium,
        Low
    }

    public enum TicketStatus
    {
        Open,
        InProgress,
        Closed
    }

    [FirestoreData]
    public class User
    {
        [FirestoreProperty]
        public string Email { get; set; } = string.Empty;

        [FirestoreProperty]
        public UserRole Role { get; set; }

        [FirestoreProperty]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public enum UserRole
    {
        User = 0,
        Technician = 1
    }
}