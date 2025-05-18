using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Google.Cloud.Storage.V1;
using Google.Apis.Auth.OAuth2;
using TicketingSystem.Services;
using Google.Cloud.Firestore;
using System.Net;
using Google.Protobuf.WellKnownTypes;
using Google.Apis.Storage.v1.Data;

namespace TicketingSystem.Controllers
{
    public class NewTicketController : Controller
    {
        private readonly string _bucketName;
        private readonly ILogger<NewTicketController> _logger;
        private readonly string _googleCredentialsJson;
        private readonly PubSubService _pubSubService;
        private readonly List<string> allowedUsers = new List<string>();

        public NewTicketController(ILogger<NewTicketController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _bucketName = "ticketing-system-bucketstore";
            _pubSubService = new PubSubService();
            _googleCredentialsJson = LoadCredentialJsonFromFile();

            var credential = GoogleCredential.FromJson(_googleCredentialsJson);

            LoadTechnicians();
        }

        private async Task LoadTechnicians()
        {
            var credential = GoogleCredential.FromJson(_googleCredentialsJson);
            var _firestoreDb = new FirestoreDbBuilder { ProjectId = "pftc-2025-leon", Credential = credential }.Build();

            CollectionReference usersCollection = _firestoreDb.Collection("users");
            Query query = usersCollection.WhereEqualTo("role", "technician");

            List<string> technicianEmails = new List<string>();
            QuerySnapshot snapshot = await query.GetSnapshotAsync();

            foreach (DocumentSnapshot document in snapshot.Documents)
            {
                if (document.Exists)
                {
                    allowedUsers.Add(document.Id);
                }
            }
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
        public async Task<IActionResult> Submit(string title, string description, string priority, IFormFileCollection attachments)
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

                            //give access to the user who raised the ticket and all techs
                            allowedUsers.Add(userEmail);

                            var aclList = allowedUsers.Select(email => new ObjectAccessControl
                            {
                                Entity = $"user-{email}",
                                Role = "READER"
                            }).ToList();

                            await storageClient.UpdateObjectAsync(new Google.Apis.Storage.v1.Data.Object
                            {
                                Bucket = _bucketName,
                                Name = objectName,
                                Acl = aclList
                            });

                            var imageUrl = $"https://storage.cloud.google.com/{_bucketName}/{objectName}?authuser=1";
                            imageUrls.Add(imageUrl);
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("No attachments provided");
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