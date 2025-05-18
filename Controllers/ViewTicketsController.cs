using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using StackExchange.Redis;
using TicketingSystem.Services;
using TicketingSystem.Models;
using Google.Cloud.Firestore;
using Google.Apis.Auth.OAuth2;

namespace TicketingSystem.Controllers
{
    public class ViewTicketsController : Controller
    {
        private readonly PubSubService _pubSubService;
        private readonly IConnectionMultiplexer _redisConnection;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromDays(7);
        private const string CacheKey = "tickets:all";
        private readonly string _googleCredentialsJson;
        private readonly FirestoreDb _firestoreDb;

        public ViewTicketsController(
        PubSubService pubSubService, IConnectionMultiplexer redisConnection)
        {
            _pubSubService = pubSubService;
            _redisConnection = redisConnection;

            _googleCredentialsJson = LoadCredentialJsonFromFile();
            var credential = GoogleCredential.FromJson(_googleCredentialsJson);
            _firestoreDb = new FirestoreDbBuilder { ProjectId = "pftc-2025-leon", Credential = credential }.Build();
        }

        private string LoadCredentialJsonFromFile()
        {
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            var filePath = Path.Combine(basePath, "pftc-2025-leon-c6d5aa81fcc1.json");

            return System.IO.File.ReadAllText(filePath);
        }

        public async Task<IActionResult> Index()
        {
            var tickets = await GetTicketsFromCache();
            var sortedTickets = tickets.OrderBy(t => t.Priority).ToList();

            return View(sortedTickets);
        }

        [HttpGet]
        public async Task FetchNewTicketsAndUpdateCache()
        {
            var pubSubMessages = await _pubSubService.FetchMessagesAsync();

            if (pubSubMessages == null || !pubSubMessages.Any())
            {
                return;
            }

            var existingTickets = await GetTicketsFromCache();
            var existingIds = new HashSet<string>(existingTickets.Select(t => t.TicketId));

            var newTickets = new List<TicketsViewModel>();
            foreach (var message in pubSubMessages)
            {
                var messageData = message.Data.ToStringUtf8();
                var ticket = JsonSerializer.Deserialize<TicketsViewModel>(messageData);

                if (ticket != null && !existingIds.Contains(ticket.TicketId))
                {
                    newTickets.Add(ticket);
                    existingIds.Add(ticket.TicketId);

                    await MailgunService.SendEmailAsync(
                        $"New Ticket | ID: {ticket.TicketId}", 
                        $"Dear Technicians, \r\n\r\n A new Ticket has been raised by {ticket.SubmittedByEmail} regarding `{ticket.Title}`",
                        ticket.TicketId
                        );
                }
            }

            if (newTickets.Any())
            {
                existingTickets.AddRange(newTickets);

                await SaveTicketsToCache(existingTickets);
            }
        }

        private async Task<List<TicketsViewModel>> GetTicketsFromCache()
        {
            var db = _redisConnection.GetDatabase();
            var cachedData = await db.StringGetAsync(CacheKey);
            if (cachedData.IsNullOrEmpty)
            {
                return new List<TicketsViewModel>();
            }

            var tickets = JsonSerializer.Deserialize<List<TicketsViewModel>>(cachedData);
            if (tickets == null)
            {
                return new List<TicketsViewModel>();
            }

            return tickets;
        }

        private async Task SaveTicketsToCache(List<TicketsViewModel> tickets)
        {
            var db = _redisConnection.GetDatabase();
            var serializedTickets = JsonSerializer.Serialize(tickets);
            await db.StringSetAsync(CacheKey, serializedTickets, _cacheExpiration);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CloseCachedTicket(string ticketId)
        {
            var tickets = await GetTicketsFromCache();

            var ticketToRemove = tickets.FirstOrDefault(t => t.TicketId == ticketId);
            if (ticketToRemove != null)
            {
                tickets.Remove(ticketToRemove);

                await SaveTicketsToCache(tickets);

                await ArchiveTicket(ticketToRemove.TicketId, ticketToRemove.Title,
                    ticketToRemove.Description, ticketToRemove.Priority,
                    ticketToRemove.SubmittedAt, ticketToRemove.SubmittedByEmail,
                    ticketToRemove.imageUrls);
            }

            return RedirectToAction("Index");
        }

        public async Task ArchiveTicket(string ticketId, string title, string description, string priority, DateTime submittedat, string userEmail, List<string> imageUrls)
        {
            var firestoreDict = new Dictionary<string, object>
            {
                ["TicketId"] = ticketId,
                ["Title"] = title,
                ["Description"] = description,
                ["Priority"] = priority,
                ["Status"] = "Closed",
                ["SubmittedAt"] = submittedat,
                ["SubmittedByEmail"] = userEmail,
                ["ImageUrls"] = imageUrls
            };

            DocumentReference docRef = _firestoreDb.Collection($"tickets_{userEmail}").Document(ticketId);
            await docRef.SetAsync(firestoreDict);
        }

    }
}