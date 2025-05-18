using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using TicketingSystem.Models;
using TicketingSystem.Services;

namespace TicketingSystem.Controllers;
public class HomeController : Controller
{
    private readonly PubSubService _pubSubService;
    private readonly FirestoreDb _firestoreDb;
    private readonly string _googleCredentialsJson;

    public HomeController()
    {
        _pubSubService = new PubSubService();

        _googleCredentialsJson = LoadCredentialJsonFromFile();
        var credential = GoogleCredential.FromJson(_googleCredentialsJson);
        _firestoreDb = new FirestoreDbBuilder
        {
            ProjectId = "pftc-2025-leon",
            Credential = credential
        }.Build();
    }

    private string LoadCredentialJsonFromFile()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var filePath = Path.Combine(basePath, "pftc-2025-leon-c6d5aa81fcc1.json");

        return System.IO.File.ReadAllText(filePath);
    }

    public async Task<IActionResult> Index()
    {
        if (!User.Identity.IsAuthenticated)
        {
            return RedirectToAction("Index", "Login", new { area = "" });
        }

        var userEmail = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Email)?.Value;

        var tickets = await GetUserTicketsFromFirestore(userEmail);
        var isTechnician = await IsUserTechnician(userEmail);

        var viewModel = new HomeViewModel
        {
            Tickets = tickets,
            IsTechnician = isTechnician
        };

        return View(viewModel);
    }

    private async Task<List<TicketsViewModel>> GetUserTicketsFromFirestore(string userEmail)
    {
        var tickets = new List<TicketsViewModel>();

        Query query = _firestoreDb.Collection($"tickets_{userEmail}");

        QuerySnapshot querySnapshot = await query.GetSnapshotAsync();

        foreach (DocumentSnapshot document in querySnapshot.Documents)
        {
            if (document.Exists)
            {
                Dictionary<string, object> ticketData = document.ToDictionary();

                var ticket = new TicketsViewModel
                {
                    TicketId = ticketData.GetValueOrDefault("TicketId", string.Empty).ToString(),
                    Title = ticketData.GetValueOrDefault("Title", string.Empty).ToString(),
                    Description = ticketData.GetValueOrDefault("Description", string.Empty).ToString(),
                    Priority = ticketData.GetValueOrDefault("Priority", string.Empty).ToString(),
                    Status = ticketData.GetValueOrDefault("Status", "Unknown").ToString(),
                    SubmittedAt = ticketData.ContainsKey("SubmittedAt") && ticketData["SubmittedAt"] is Timestamp timestamp
                        ? timestamp.ToDateTime()
                        : DateTime.MinValue,
                    imageUrls = ticketData.ContainsKey("ImageUrls") && ticketData["ImageUrls"] is List<object> urls
                        ? urls.Select(u => u.ToString()).ToList()
                        : new List<string>()
                };

                tickets.Add(ticket);
            }
        }

        return tickets;
    }

    private async Task<bool> IsUserTechnician(string userEmail)
    {
        DocumentReference docRef = _firestoreDb.Collection("users").Document(userEmail);

        DocumentSnapshot document = await docRef.GetSnapshotAsync();

        if (document.Exists)
        {
            var data = document.ToDictionary();

            if (data.TryGetValue("role", out object roleValue) && roleValue as string == "technician")
            {
                return true;
            }
        }

        return false;
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
