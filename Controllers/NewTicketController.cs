using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Google.Cloud.Storage.V1;
using Google.Apis.Auth.OAuth2;
using TicketingSystem.Services;
using Google.Cloud.Firestore;
using System.Net;

namespace TicketingSystem.Controllers
{
    public class NewTicketController : Controller
    {
        private readonly string _bucketName;
        private readonly ILogger<NewTicketController> _logger;
        private readonly string _googleCredentialsJson;
        private readonly PubSubService _pubSubService;
        private readonly FirestoreDb _firestoreDb;

        public NewTicketController(ILogger<NewTicketController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _bucketName = "ticketing-system-bucketstore";

            _pubSubService = new PubSubService();

            _googleCredentialsJson = LoadCredentialJsonFromFile();

            var credential = GoogleCredential.FromJson(_googleCredentialsJson);
            _firestoreDb = new FirestoreDbBuilder{ProjectId = "pftc-2025-leon", Credential = credential}.Build();
        }

        private string LoadCredentialJsonFromFile()
        {
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            var filePath = Path.Combine(basePath, "pftc-2025-leon-c6d5aa81fcc1.json");

            return System.IO.File.ReadAllText(filePath);
        }

        public IActionResult Index()
        {
            if (!User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Login", new { area = "" });
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(
            string title,
            string description,
            string priority,
            IFormFileCollection attachments)
        {
            try
            {
                _logger.LogInformation("Submit method started");
                var userEmail = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Email)?.Value;
                if (string.IsNullOrEmpty(userEmail))
                {
                    _logger.LogWarning("User not authenticated");
                    return Unauthorized("User not authenticated.");
                }

                var ticketId = Guid.NewGuid().ToString();
                Console.WriteLine(ticketId);
                var timestamp = DateTime.UtcNow;
                var imageUrls = new List<string>();

                if (attachments != null && attachments.Count > 0)
                {
                    StorageClient storageClient;

                    var credential = GoogleCredential.FromJson(_googleCredentialsJson)
                            .CreateScoped(Google.Apis.Storage.v1.StorageService.Scope.DevstorageFullControl);

                    storageClient = StorageClient.Create(credential);

                    var bucket = await storageClient.GetBucketAsync(_bucketName);

                    foreach (var file in attachments)
                    {
                        if (file.Length > 0 && IsImage(file.ContentType))
                        {
                            var uniqueFileName = $"{ticketId}_{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
                            var objectName = $"tickets/{uniqueFileName}";

                            using var memoryStream = new MemoryStream();
                            await file.CopyToAsync(memoryStream);
                            memoryStream.Position = 0;

                            var uploadedObject = await storageClient.UploadObjectAsync(
                                bucket: _bucketName,
                                objectName: objectName,
                                contentType: file.ContentType,
                                source: memoryStream);

                            var imageUrl = $"https://storage.googleapis.com/{_bucketName}/{objectName}";
                            imageUrls.Add(imageUrl);
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("No attachments provided");
                }

                var firestoreDict = new Dictionary<string, object>
                {
                    ["TicketId"] = ticketId,
                    ["Title"] = title,
                    ["Description"] = description,
                    ["Priority"] = priority,
                    ["Status"] = "Open",
                    ["SubmittedAt"] = timestamp,
                    ["SubmittedByEmail"] = userEmail,
                    ["ImageUrls"] = imageUrls
                };

                try
                {
                    DocumentReference docRef = _firestoreDb.Collection($"tickets_{userEmail}").Document(ticketId);
                    await docRef.SetAsync(firestoreDict);
                    _logger.LogInformation($"Ticket {ticketId} saved to Firestore");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error saving to Firestore: {ex.Message}");
                }

                var ticketData = new
                {
                    TicketId = ticketId,
                    Title = title,
                    Description = description,
                    Priority = priority,
                    Status = "Open",
                    SubmittedAt = timestamp.ToString("o"),
                    SubmittedByEmail = userEmail,
                    ImageUrls = imageUrls
                };

                var jsonPayload = JsonSerializer.Serialize(ticketData);

                try
                {
                    await _pubSubService.PublishToPubSub(jsonPayload, priority);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error publishing to Pub/Sub: {ex.Message}");
                }

                return Ok(new
                {
                    Message = "Ticket submitted successfully!",
                    TicketId = ticketId,
                    ImageUrls = imageUrls
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error in Submit: {ex.Message}");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        private bool IsImage(string contentType)
        {
            return !string.IsNullOrEmpty(contentType) && contentType.StartsWith("image/");
        }

    }
}