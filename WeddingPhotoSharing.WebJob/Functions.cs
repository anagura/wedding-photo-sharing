using Microsoft.Azure.WebJobs;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Auth;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LineMessaging;

namespace WeddingPhotoSharing.WebJob
{
    public class Functions
    {
        static readonly HttpClient httpClient = new HttpClient()
        {
            BaseAddress = new Uri("https://hooks.slack.com/"),
            Timeout = TimeSpan.FromSeconds(10)
        };

        private static readonly string SlackWebhookPath = AppSettings.SlackWebhookPath;
        private static readonly string WebsocketServerUrl = AppSettings.WebsocketServerUrl;
        private static readonly string LineAccessToken = AppSettings.LineAccessToken;
        private static readonly string LineMediaContainerName = AppSettings.LineMediaContainerName;
        private static readonly string StorageAccountName = AppSettings.StorageAccountName;
        private static readonly string StorageAccountKey = AppSettings.StorageAccountKey;

        private static readonly ClientWebSocket webSocket;
        private static readonly ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();
        private static readonly LineMessagingClient lineMessagingClient;

        private static readonly StorageCredentials storageCredentials;
        private static readonly CloudStorageAccount storageAccount;
        private static readonly CloudBlobClient blobClient;
        private static readonly CloudBlobContainer container;

        static Functions()
        {
            webSocket = new ClientWebSocket();
            webSocket.Options.KeepAliveInterval = TimeSpan.FromMinutes(1);
            TryConnect(Console.Out);

            lineMessagingClient = new LineMessagingClient(LineAccessToken);

            storageCredentials = new StorageCredentials(StorageAccountName, StorageAccountKey);
            storageAccount = new CloudStorageAccount(storageCredentials, true);
            blobClient = storageAccount.CreateCloudBlobClient();
            container = blobClient.GetContainerReference(LineMediaContainerName);
        }

        public async static Task ProcessQueueMessage([QueueTrigger("line-bot-workitems")] string message, TextWriter log)
        {
            if (webSocket.State != WebSocketState.Open)
            {
                TryConnect(log);

                // send at next chance
                messageQueue.Enqueue(message);
                return;
            }

            if (messageQueue.Count > 0)
            {
                while (messageQueue.TryDequeue(out string oldMessage))
                {
                    log.WriteLine("old message: " + oldMessage);
                    await PostToSlack(message, log);
                    await PostToWebsocket(message, log);
                }
            }
            log.WriteLine(message);

            var websocket_message = await GetContentFromLine(message, log);
            log.WriteLine(websocket_message);
            message = websocket_message;

            await PostToSlack(message, log);
            await PostToWebsocket(message, log);
        }

        private static async Task<string> GetContentFromLine(string message, TextWriter log)
        {
            List<WebSocketMessage> lineMessages = new List<WebSocketMessage>();
            LineWebhookContent content = JsonConvert.DeserializeObject<LineWebhookContent>(message);
            foreach (LineWebhookContent.Event event_message in content.Events )
            {
                if (event_message.Type == WebhookRequestEventType.Message
                    && event_message.Source.Type == WebhookRequestSourceType.User)
                {
                    WebSocketMessage result = new WebSocketMessage();

                    string userId = event_message.Source.UserId;
                    var profile = await lineMessagingClient.GetProfile(userId);
                    result.Name = profile.DisplayName;

                    if (event_message.Message.Type == MessageType.Text)
                    {
                        result.Text = event_message.Message.Text;
                        // TODO:画像化する
                    }
                    else if (event_message.Message.Type == MessageType.Image)
                    {
                        var image = lineMessagingClient.GetMessageContent(event_message.Message.Id.ToString());

                        var fileName = event_message.Message.Id.ToString() + ".jpg";
                        CloudBlockBlob blockBlob = container.GetBlockBlobReference(fileName);
                        blockBlob.Properties.ContentType = "image/jpeg";

                        await blockBlob.UploadFromByteArrayAsync(image.Result, 0, image.Result.Length);

                        var filePath = string.Format("https://{0}.blob.core.windows.net/{1}/{2}", StorageAccountName, LineMediaContainerName, fileName);
                        result.ImageUrl = filePath;
                    }
                    lineMessages.Add(result);
                }
            }

            return JsonConvert.SerializeObject(lineMessages);
        }

        private static void TryConnect(TextWriter log)
        {
            try
            {
                // set timeout
                var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                webSocket.ConnectAsync(new Uri(WebsocketServerUrl), cancellationTokenSource.Token).FireAndForget(log);
            }
            catch (Exception ex)
            {
                log.WriteLine("ConnectAndForget: " + ex.ToString());
            }
        }

        private static Task PostToSlack(string message, TextWriter log)
        {
            try
            {
                var slackMessage = new SlackMessage
                {
                    Text = $"```{message}```"
                };
                var json = JsonConvert.SerializeObject(slackMessage);
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    return httpClient.PostAsync(SlackWebhookPath, content);
                }
            }
            catch (Exception ex)
            {
                log.WriteLine("PostToSlack: " + ex.ToString());
                return Task.CompletedTask;
            }
        }

        private static Task PostToWebsocket(string message, TextWriter log)
        {
            try
            {
                var messageBytes = Encoding.UTF8.GetBytes(message);

                // set timeout
                var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                const int bufferSize = 1024 * 4;
                var list = new List<Task>();
                foreach (var (buffer, index) in messageBytes.Buffer(bufferSize).Indexed())
                {
                    var chunk = buffer.ToArray();
                    var endOfMessage = ((double)messageBytes.Length / bufferSize).Floor() == index;
                    list.Add(webSocket.SendAsync(new ArraySegment<byte>(chunk, 0, chunk.Length), WebSocketMessageType.Text, endOfMessage, cancellationTokenSource.Token));
                }
                return Task.WhenAll(list);
            }
            catch (Exception ex)
            {
                log.WriteLine("PostToWebsocket: " + ex.ToString());
                return Task.CompletedTask;
            }
        }
    }

    public class WebSocketMessage
    {
        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("imageUrl")]
        public string ImageUrl { get; set; }
    }

    public class SlackMessage
    {
        [JsonProperty("text")]
        public string Text { get; set; }
    }
}
