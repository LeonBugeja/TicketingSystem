using System.Net.Mail;
using System.Net;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.Cloud.Logging.V2;
using Google.Api;
using Google.Cloud.Logging.Type;

namespace TicketingSystem.Services
{
    public class MailgunService
    {
        public static async Task SendEmailAsync(string subject, string body, string ticketId)
        {
            var _googleCredentialsJson = LoadCredentialJsonFromFile();
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
                    technicianEmails.Add(document.Id);
                }
            }


            var smtpClient = new SmtpClient("smtp.mailgun.org")
            {
                Port = 587,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential("mailgun-pftc-2025@sandbox6019a7d312304ff3ae2681c2467c1d9c.mailgun.org", "07(z3"),
                EnableSsl = true,
            };

            MailMessage mail = new MailMessage();
            mail.From = new MailAddress("leon.bugeja1@gmail.com");
            mail.Subject = subject;
            mail.Body = body;

            foreach(var email in technicianEmails)
            {
                mail.To.Add(email);

                //log each email
                await LogEmailSentAsync(ticketId, email, subject);
            }

            smtpClient.Send(mail);
        }
        private static string LoadCredentialJsonFromFile()
        {
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            var filePath = Path.Combine(basePath, "pftc-2025-leon-c6d5aa81fcc1.json");

            return System.IO.File.ReadAllText(filePath);
        }

        private static async Task LogEmailSentAsync(string ticketId, string recipient, string subject)
        {
            var _googleCredentialsJson = LoadCredentialJsonFromFile();
            var credential = GoogleCredential.FromJson(_googleCredentialsJson);

            var builder = new LoggingServiceV2ClientBuilder
            {
                Credential = credential
            };

            var loggerClient = await builder.BuildAsync();

            var logName = new LogName("pftc-2025-leon", "email_logs");
            var severity = LogSeverity.Info;

            var entry = new LogEntry
            {
                LogNameAsLogName = logName,
                Severity = severity,
                TextPayload = $"Sent email: '{subject}' to {recipient}",
                Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow).ToProto(),
                Resource = new MonitoredResource
                {
                    Type = "global"
                },
                Labels = {["ticket_id"] = ticketId, ["recipient"] = recipient},
                //trace = correlation
                Trace = $"projects/pftc-2025-leon/traces/{ticketId}"
            };

            await loggerClient.WriteLogEntriesAsync(new WriteLogEntriesRequest
            {
                Entries = { entry },
                LogNameAsLogName = logName,
                Resource = entry.Resource,
                Labels = { { "ticket_id", ticketId } }
            });
        }
    }
}
