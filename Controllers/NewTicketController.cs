using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Google.Cloud.PubSub.V1;
using Google.Cloud.Storage.V1;
using Google.Protobuf;
using Google.Apis.Auth.OAuth2;
using Grpc.Auth;

namespace TicketingSystem.Controllers
{
    public class NewTicketController : Controller
    {
        private readonly string _bucketName;
        private readonly string _projectId;
        private const string TopicId = "tickets-topic";
        private readonly ILogger<NewTicketController> _logger;
        private readonly string _googleCredentialsJson;

        public NewTicketController(ILogger<NewTicketController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _bucketName = "ticketing-system-bucketstore";
            _projectId = "pftc-2025-leon";

            _googleCredentialsJson = LoadCredentialJsonFromFile();
        }

        private string LoadCredentialJsonFromFile()
        {
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            var filePath = Path.Combine(basePath, "pftc-2025-leon-c6d5aa81fcc1.json");

            return System.IO.File.ReadAllText(filePath);
        }

        public IActionResult Index()
        {
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
                var userEmail = User.Identity?.Name;
                if (string.IsNullOrEmpty(userEmail))
                {
                    _logger.LogWarning("User not authenticated");
                    return Unauthorized("User not authenticated.");
                }

                var ticketId = Guid.NewGuid().ToString();
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

                var ticketData = new
                {
                    TicketId = ticketId,
                    Title = title,
                    Description = description,
                    Priority = priority,
                    Status = "Queued",
                    SubmittedAt = timestamp.ToString("o"),
                    SubmittedByEmail = userEmail,
                    ImageUrls = imageUrls
                };

                var jsonPayload = JsonSerializer.Serialize(ticketData);

                try
                {
                    await PublishToPubSub(jsonPayload, priority);
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

        private async Task PublishToPubSub(string message, string priority)
        {
            var topicName = TopicName.FromProjectTopic(_projectId, TopicId);

            PublisherClient publisher;

            var credential = GoogleCredential.FromJson(_googleCredentialsJson)
                    .CreateScoped(PublisherServiceApiClient.DefaultScopes);

            var channelCreds = credential.ToChannelCredentials();

            publisher = await PublisherClient.CreateAsync(topicName,
                new PublisherClient.ClientCreationSettings(credentials: channelCreds));

            string normalizedPriority = priority?.ToLower() ?? "medium";
            if (normalizedPriority != "high" && normalizedPriority != "medium" && normalizedPriority != "low")
            {
                normalizedPriority = "Medium"; // Default to medium if invalid value
                _logger.LogWarning($"Invalid priority value '{priority}' normalized to 'Medium'");
            }

            var pubsubMessage = new PubsubMessage
            {
                Data = ByteString.CopyFromUtf8(message),
                Attributes =
                {
                    { "priority", normalizedPriority }
                }
            };

            string messageId = await publisher.PublishAsync(pubsubMessage);
        }

    }
}