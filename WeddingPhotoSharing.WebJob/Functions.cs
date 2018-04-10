using Microsoft.Azure.WebJobs;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Auth;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LineMessaging;
using ImageGeneration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;


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

        private static readonly string ImageGeneratorTemplate;

        private static ClientWebSocket webSocket;
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

            ImageGeneratorTemplate = File.ReadAllText("TextTemplates/TextPanel.xaml");
        }

        public async static Task ProcessQueueMessage([QueueTrigger("line-bot-workitems")] string message, TextWriter log)
        {
            if (webSocket.State != WebSocketState.Open
                && webSocket.State != WebSocketState.Connecting)
            {
                log.WriteLine("websocket state:" + webSocket.State);
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
                    oldMessage = await GetContentFromLine(oldMessage, log);
                    await PostToSlack(oldMessage, log);
                    await PostToWebsocket(oldMessage, log);
                }
            }

            log.WriteLine("before :" + message);
            message = await GetContentFromLine(message, log);
            log.WriteLine("after :" + message);

            await PostToSlack(message, log);
            await PostToWebsocket(message, log);
        }

        private static async Task<string> GetContentFromLine(string message, TextWriter log)
        {
            List<WebSocketMessage> lineMessages = new List<WebSocketMessage>();
            LineWebhookContent content = JsonConvert.DeserializeObject<LineWebhookContent>(message);
            foreach (LineWebhookContent.Event eventMessage in content.Events )
            {
                if (eventMessage.Type == WebhookRequestEventType.Message
                    && eventMessage.Source.Type == WebhookRequestSourceType.User)
                {
                    WebSocketMessage result = new WebSocketMessage();

                    string userId = eventMessage.Source.UserId;
                    var profile = await lineMessagingClient.GetProfile(userId);
                    result.Name = profile.DisplayName;
                    result.MessageType = (int)eventMessage.Message.Type;
                    var ext = eventMessage.Message.Type == MessageType.Video ? ".mpg" : ".jpg";
                    var fileName = eventMessage.Message.Id.ToString() + ext;

                    byte[] image;
                    if (eventMessage.Message.Type == MessageType.Text)
                    {
                        // テキストを画像化
                        dynamic viewModel = new ExpandoObject();
                        viewModel.Name = result.Name;
                        viewModel.Text = eventMessage.Message.Text;
                        image = ImageGenerator.GenerateImage(ImageGeneratorTemplate, viewModel);

                        // 画像をストレージにアップロード
                        UploadImageToStorage(fileName, image);
                        result.ImageUrl = GetUrl(fileName);
                    }
                    else if (eventMessage.Message.Type == MessageType.Image)
                    {
                        // LINEから画像を取得
                        var lineResult = lineMessagingClient.GetMessageContent(eventMessage.Message.Id.ToString());

                        // 画像をストレージにアップロード
                        UploadImageToStorage(fileName, lineResult.Result);
                        result.ImageUrl = GetUrl(fileName);

                        // サムネイル
                        var thumbnailFileName = string.Format("thunmnail_{0}{1}", eventMessage.Message.Id, ext);
                        ResizeUpload(thumbnailFileName, lineResult.Result, 256);
                        result.ThumbnailImageUrl = GetUrl(thumbnailFileName);

                    }
                    else if (eventMessage.Message.Type == MessageType.Video)
                    {
                        // LINEから画像を取得
                        var lineResult = lineMessagingClient.GetMessageContent(eventMessage.Message.Id.ToString());

                        // 画像をストレージにアップロード
                        UploadVideoToStorage(fileName, lineResult.Result);
                        result.ImageUrl = GetUrl(fileName);
                    }
                    else
                    {
                        log.WriteLine("not supported message type:" + eventMessage.Message.Type);
                        continue;
                    }

                    lineMessages.Add(result);
                }
            }

            return JsonConvert.SerializeObject(lineMessages);
        }

        private static void ResizeUpload(string fileName, byte[] sourceImage, int width)
        {
            using (Image<Rgba32> image = SixLabors.ImageSharp.Image.Load(sourceImage))
            {
                if (image.Width > width)
                {
                    int ratio = width / image.Width;
                    image.Mutate(x => x
                                 .Resize(width, image.Height * ratio));
                }

                using (var ms = new MemoryStream())
                {
                    image.SaveAsJpeg(ms);
                    ms.Position = 0;    //Move the pointer to the start of stream.
                    UploadStreamImageToStorage(fileName, ms);
                }
            }
        }

        private static async void UploadStreamImageToStorage(string fileName, Stream stream)
        {
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(fileName);
            blockBlob.Properties.ContentType = "image/jpeg";

            await blockBlob.UploadFromStreamAsync(stream);
        }

        private static async void UploadImageToStorage(string fileName, byte[] image)
        {
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(fileName);
            blockBlob.Properties.ContentType = "image/jpeg";

            await blockBlob.UploadFromByteArrayAsync(image, 0, image.Length);
        }

        private static async void UploadVideoToStorage(string fileName, byte[] image)
        {
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(fileName);
            blockBlob.Properties.ContentType = "video/mpeg";

            await blockBlob.UploadFromByteArrayAsync(image, 0, image.Length);
        }

        private static string GetUrl(string fileName)
        {
            return string.Format("https://{0}.blob.core.windows.net/{1}/{2}", StorageAccountName, LineMediaContainerName, fileName);
        }

        private static void TryConnect(TextWriter log)
        {
            try
            {
                // set timeout
                webSocket = new ClientWebSocket();
                webSocket.Options.KeepAliveInterval = TimeSpan.FromMinutes(1);

                var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                webSocket.ConnectAsync(new Uri(WebsocketServerUrl), cancellationTokenSource.Token).FireAndForget(log);
            }
            catch (Exception ex)
            {
                log.WriteLine("ConnectAndForget: " + ex.ToString());
            }
        }

        private static async Task PostToSlack(string message, TextWriter log)
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
                    await httpClient.PostAsync(SlackWebhookPath, content);
                }
            }
            catch (Exception ex)
            {
                log.WriteLine("PostToSlack: " + ex.ToString());
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
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("messageType")]
        public int MessageType { get; set; }

        [JsonProperty("imageUrl")]
        public string ImageUrl { get; set; }

        [JsonProperty("thumbnailImageUrl")]
        public string ThumbnailImageUrl { get; set; }
    }

    public class SlackMessage
    {
        [JsonProperty("text")]
        public string Text { get; set; }
    }
}
