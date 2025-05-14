// Services/IRedisService.cs
using JakeScerriPFTC_Assignment.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace JakeScerriPFTC_Assignment.Services
{
    public interface IRedisService
    {
        Task SaveTicketAsync(Ticket ticket);
        Task<List<Ticket>> GetTechnicianTicketsAsync();
        Task<Ticket> GetTicketAsync(string ticketId);
        Task CloseTicketAsync(string ticketId, string technicianEmail, FirestoreService firestoreService);
        bool IsConnected();
        string GetConnectionInfo();
    }
}