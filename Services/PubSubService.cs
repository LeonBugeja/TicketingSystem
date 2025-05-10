using Google.Cloud.PubSub.V1;
using Newtonsoft.Json;
using Google.Apis.Auth.OAuth2;
using Grpc.Auth;
using Google.Protobuf;

namespace TicketingSystem.Services
{
    public class PubSubService
    {
        private readonly string _projectId;
        private readonly string _topicId;
        private readonly string _subscriptionId;
        private readonly string _googleCredentialsJson;

        public PubSubService()
        {
            _projectId = "pftc-2025-leon";
            _topicId = "tickets-topic";
            _subscriptionId = "tickets-topic-sub";
            _googleCredentialsJson = LoadCredentialJsonFromFile();
            var credential = GoogleCredential.FromJson(_googleCredentialsJson)
                            .CreateScoped(Google.Apis.Storage.v1.StorageService.Scope.DevstorageFullControl);
        }

        private string LoadCredentialJsonFromFile()
        {
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            var filePath = Path.Combine(basePath, "pftc-2025-leon-c6d5aa81fcc1.json");

            return System.IO.File.ReadAllText(filePath);
        }

        public async Task PublishToPubSub(string message, string priority)
        {
            var topicName = TopicName.FromProjectTopic(_projectId, _topicId);

            PublisherClient publisher;

            var credential = GoogleCredential.FromJson(_googleCredentialsJson)
                    .CreateScoped(PublisherServiceApiClient.DefaultScopes);

            var channelCreds = credential.ToChannelCredentials();

            publisher = await PublisherClient.CreateAsync(topicName, new PublisherClient.ClientCreationSettings(credentials: channelCreds));

            string normalizedPriority = priority?.ToLower() ?? "medium";
            if (normalizedPriority != "high" && normalizedPriority != "medium" && normalizedPriority != "low")
            {
                normalizedPriority = "Medium";
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

        public async Task<List<PubsubMessage>> FetchMessagesAsync(int durationInSeconds = 7, bool acknowledge = true)
        {
            var subscriptionName = SubscriptionName.FromProjectSubscription(_projectId, _subscriptionId);
            var messages = new List<PubsubMessage>();

            var credential = GoogleCredential.FromJson(_googleCredentialsJson)
                    .CreateScoped(PublisherServiceApiClient.DefaultScopes);
            var channelCreds = credential.ToChannelCredentials();

            SubscriberClient subscriber = await SubscriberClient.CreateAsync(subscriptionName, new SubscriberClient.ClientCreationSettings(credentials: channelCreds));

            Task startTask = subscriber.StartAsync(async (PubsubMessage message, CancellationToken cancel) =>
            {
                try
                {
                    messages.Add(message);

                    string text = message.Data.ToStringUtf8();

                    if (message.Attributes != null)
                    {
                        foreach (var attribute in message.Attributes)
                        {
                            //Console.WriteLine($"{attribute.Key} = {attribute.Value}");
                        }
                    }

                    return acknowledge ? SubscriberClient.Reply.Ack : SubscriberClient.Reply.Nack;
                }
                catch (Exception ex)
                {
                    return SubscriberClient.Reply.Nack;
                }
            });

            await Task.Delay(TimeSpan.FromSeconds(durationInSeconds));

            await subscriber.StopAsync(CancellationToken.None);

            await startTask;
            return messages
            .OrderBy(m => {
                if (m.Attributes != null && m.Attributes.ContainsKey("priority"))
                {
                    string priority = m.Attributes["priority"];

                    return priority switch
                    {
                        "High" => 1,
                        "Medium" => 2,
                        "Low" => 3,
                        _ => 4
                    };
                }

                return 5;
            })
            .ToList();
        }

    }
}