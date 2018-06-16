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
using System.Net.Http.Headers;
using System.Linq;
using Microsoft.WindowsAzure.Storage.Table;

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
        private static readonly string LineAdultMediaContainerName = AppSettings.LineAdultMediaContainerName;
        private static readonly string LineMessageTableName = AppSettings.LineMessageTableName;
        private static readonly string StorageAccountName = AppSettings.StorageAccountName;
        private static readonly string StorageAccountKey = AppSettings.StorageAccountKey;
        private static readonly string VisionSubscriptionKey = AppSettings.VisionSubscriptionKey;

        private static readonly string VisionUrl = "https://southeastasia.api.cognitive.microsoft.com/vision/v2.0/analyze";

        private static ClientWebSocket webSocket;
        private static readonly ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();
        private static readonly LineMessagingClient lineMessagingClient;

        // Azure Storage
        private static readonly StorageCredentials storageCredentials;
        private static readonly CloudStorageAccount storageAccount;

        // 画像格納のBlob
        private static readonly CloudBlobClient blobClient;
        private static readonly CloudBlobContainer container;
        private static readonly CloudBlobContainer adultContainer;

        // メッセージ格納のTable
        private static readonly CloudTableClient tableClient;
        private static readonly CloudTable table;

        private const int ImageHeight = 300;
        private const int MessageLength = 40;

        private static readonly string TemplateDirectoryName = "TextTemplates";
        private static List<TextImageTemplate> _imageTemplates = new List<TextImageTemplate>();


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
            adultContainer = blobClient.GetContainerReference(LineAdultMediaContainerName);

            tableClient = storageAccount.CreateCloudTableClient();
            table = tableClient.GetTableReference(LineMessageTableName);
            table.CreateIfNotExists();

            _imageTemplates.Add(new TextImageTemplate { LimitSize = 8, ImageName = "12071.png", TemplateName = "Big8.xaml" });
            _imageTemplates.Add(new TextImageTemplate { LimitSize = 20, ImageName = "51881.png", TemplateName = "51881.xaml" });
            _imageTemplates.Add(new TextImageTemplate { LimitSize = 30, ImageName = "51893.png", TemplateName = "Middle10.xaml" });
            _imageTemplates.Add(new TextImageTemplate { LimitSize = 30, ImageName = "29831.png", TemplateName = "29831.xaml" });
            _imageTemplates.Add(new TextImageTemplate { LimitSize = MessageLength, ImageName = "82096.png", TemplateName = "TextPanel.xaml" });

            _imageTemplates.ForEach(x =>
            {
                x.Template = File.ReadAllText(TemplateDirectoryName + "/" + x.TemplateName);
            });
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

            try
            {
                if (messageQueue.Count > 0)
                {
                    while (messageQueue.TryDequeue(out string oldMessage))
                    {
                        log.WriteLine("old message: " + oldMessage);
                        oldMessage = await GetContentFromLine(oldMessage, log);
                        if (!string.IsNullOrEmpty(oldMessage))
                        {
  //                          await PostToSlack(oldMessage, log);
                            await PostToWebsocket(oldMessage, log);
                        }
                    }
                }

                message = await GetContentFromLine(message, log);
                if (!string.IsNullOrEmpty(message))
                {
//                    await PostToSlack(message, log);
                    await PostToWebsocket(message, log);
                }
            }catch(Exception ex)
            {
                log.WriteLine(ex.ToString());
                await PostToSlack(ex.ToString(), log);
                throw ex;   // 次回再送するため
            }
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
                    string suffix = "";

                    byte[] image;
                    if (eventMessage.Message.Type == MessageType.Text)
                    {
                        result.Message = eventMessage.Message.Text;

                        // ストレージテーブルに格納
                        await UploadMessageToStorageTable(eventMessage.Message.Id,result.Name, eventMessage.Message.Text);

                        // テキストを画像化
                        string textMessage = eventMessage.Message.Text;
                        if (textMessage.Length > MessageLength)
                        {
                            textMessage = textMessage.Substring(0, MessageLength) + "...";
                            suffix = string.Format("\nメッセージが長いため、途中までしか表示されません。{0}文字以内で入力をお願いします。", MessageLength);
                        }

                        var textLength = textMessage.Length > MessageLength ? MessageLength : textMessage.Length;
                        var template = _imageTemplates.Where(x => x.LimitSize >= textLength).PickRandom();

                        dynamic viewModel = new ExpandoObject();
                        viewModel.Name = result.Name;
                        viewModel.Text = textMessage;
                        viewModel.Source = string.Format("{0}\\{1}\\{2}", Directory.GetCurrentDirectory(), TemplateDirectoryName, template.ImageName);

                        image = ImageGenerator2.GenerateImage(template.Template, viewModel, template.ImageName);

                        // 画像をストレージにアップロード
                        await UploadImageToStorage(fileName, image);
                        result.ImageUrl = result.ThumbnailImageUrl = GetUrl(fileName);
                    }
                    else if (eventMessage.Message.Type == MessageType.Image)
                    {
                        // LINEから画像を取得
                        var lineResult = lineMessagingClient.GetMessageContent(eventMessage.Message.Id.ToString());
                        if (lineResult.Result == null || !lineResult.IsCompleted || lineResult.IsFaulted)
                        {
                            throw new Exception("GetMessageContent is null");
                        }

                        // エロ画像チェック
                        try
                        {
                            string vision_result = await MakeAnalysisRequest(lineResult.Result);

                            var vision = JsonConvert.DeserializeObject<VisionAdultResult>(vision_result);
                            if (vision.Adult.isAdultContent)
                            {
                                // アダルト用ストレージにアップロード
                                await UploadImageToStorage(fileName, lineResult.Result, true);

                                vision_result += ", imageUrl:" + GetUrl(fileName, true);
//                                await PostToSlack(vision_result, log);

//                                await ReplyToLine(eventMessage.ReplyToken, string.Format("ちょっと嫌な予感がするので、この写真は却下します。\nscore:{0}", vision.Adult.adultScore), log);
                                continue;
                            }
  //                          await PostToSlack(vision_result, log);
                        }
                        catch (Exception ex)
                        {
                            await PostToSlack(ex.ToString(), log);
                        }

                        // 画像をストレージにアップロード
                        await UploadImageToStorage(fileName, lineResult.Result);
                        result.ImageUrl = GetUrl(fileName);

                        // サムネイル
                        var thumbnailFileName = string.Format("thumbnail_{0}{1}", eventMessage.Message.Id, ext);
                        await ResizeUpload(thumbnailFileName, lineResult.Result, ImageHeight);
                        result.ThumbnailImageUrl = GetUrl(thumbnailFileName);
                    }
/*
                   else if (eventMessage.Message.Type == MessageType.Video)
                    {
                        // LINEから画像を取得
                        var lineResult = lineMessagingClient.GetMessageContent(eventMessage.Message.Id.ToString());

                        // 画像をストレージにアップロード
                        await UploadVideoToStorage(fileName, lineResult.Result);
                        result.ImageUrl = GetUrl(fileName);
                    }
 */
                    else
                    {
                        log.WriteLine("not supported message type:" + eventMessage.Message.Type);
//                        await ReplyToLine(eventMessage.ReplyToken, "未対応のメッセージです。テキストか画像を投稿してください", log);
                        continue;
                    }

//                    await ReplyToLine(eventMessage.ReplyToken, "投稿を受け付けました。表示されるまで少々お待ちください。" + suffix, log);
                    lineMessages.Add(result);
                }
            }

            return lineMessages.Any() ? JsonConvert.SerializeObject(lineMessages) : string.Empty;
        }

        private static async Task ReplyToLine(string replyToken, string message, TextWriter log)
        {
            try
            {
                await lineMessagingClient.ReplyMessage(replyToken, message);
            }
            catch (LineMessagingException lex)
            {
                log.WriteLine(string.Format("message:{0}, source:{1}, token:{2}", lex.Message, lex.Source, replyToken));
            }
            catch (Exception ex)
            {
                log.WriteLine(ex.ToString());
            }
        }


        private static async Task ResizeUpload(string fileName, byte[] sourceImage, int height)
        {
            using (Image<Rgba32> image = SixLabors.ImageSharp.Image.Load(sourceImage))
            {
                if (image.Height > height)
                {
                    int ratio = height / image.Height;
                    image.Mutate(x => x
                                 .Resize(image.Width * ratio, height));
                }

                using (var ms = new MemoryStream())
                {
                    image.SaveAsJpeg(ms);
                    ms.Position = 0;    //Move the pointer to the start of stream.
                    await UploadStreamImageToStorage(fileName, ms);
                }
            }
        }

        private static async Task UploadStreamImageToStorage(string fileName, Stream stream)
        {
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(fileName);
            blockBlob.Properties.ContentType = "image/jpeg";

            await blockBlob.UploadFromStreamAsync(stream);
        }

        private static async Task UploadMessageToStorageTable(long id, string name, string message)
        {
            // テーブルストレージに格納
            LineMessageEntity tableMessage = new LineMessageEntity(name, id.ToString());
            tableMessage.Id = id;
            tableMessage.Name = name;
            tableMessage.Message = message;

            TableOperation insertOperation = TableOperation.Insert(tableMessage);
            await table.ExecuteAsync(insertOperation);
        }

        private static async Task UploadImageToStorage(string fileName, byte[] image, bool isAdult = false)
        {
            CloudBlockBlob blockBlob = isAdult ? 
                adultContainer.GetBlockBlobReference(fileName):
                container.GetBlockBlobReference(fileName);

            blockBlob.Properties.ContentType = "image/jpeg";

            await blockBlob.UploadFromByteArrayAsync(image, 0, image.Length);
        }

        private static async Task UploadVideoToStorage(string fileName, byte[] image)
        {
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(fileName);
            blockBlob.Properties.ContentType = "video/mpeg";

            await blockBlob.UploadFromByteArrayAsync(image, 0, image.Length);
        }

        private static string GetUrl(string fileName, bool isAdult = false)
        {
            return string.Format("https://{0}.blob.core.windows.net/{1}/{2}", StorageAccountName, isAdult ? LineAdultMediaContainerName : LineMediaContainerName, fileName);
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

        private static async Task PostToWebsocket(string message, TextWriter log)
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
                    await webSocket.SendAsync(new ArraySegment<byte>(chunk, 0, chunk.Length), WebSocketMessageType.Text, endOfMessage, cancellationTokenSource.Token);
                }
            }
            catch (Exception ex)
            {
                log.WriteLine("PostToWebsocket: " + ex.ToString());
                throw ex;
            }
        }

        private static async Task<string> MakeAnalysisRequest(byte[] byteData)
        {
            string contentString = string.Empty;

            try
            {
                HttpClient client = new HttpClient();

                // Request headers.
                client.DefaultRequestHeaders.Add(
                    "Ocp-Apim-Subscription-Key", VisionSubscriptionKey);

                // Request parameters. A third optional parameter is "details".
                string requestParameters = "visualFeatures=Adult";

                // Assemble the URI for the REST API Call.
                string uri = VisionUrl + "?" + requestParameters;

                HttpResponseMessage response;

                // Request body. Posts a locally stored JPEG image.
//                byte[] byteData = GetImageAsByteArray(imageFilePath);

                using (ByteArrayContent content = new ByteArrayContent(byteData))
                {
                    // This example uses content type "application/octet-stream".
                    // The other content types you can use are "application/json"
                    // and "multipart/form-data".
                    content.Headers.ContentType =
                        new MediaTypeHeaderValue("application/octet-stream");

                    // Make the REST API call.
                    response = await client.PostAsync(uri, content);
                }

                // Get the JSON response.
                contentString = await response.Content.ReadAsStringAsync();

                // Display the JSON response.
                Console.WriteLine("\nResponse:\n");
                Console.WriteLine(JsonPrettyPrint(contentString));
            }
            catch (Exception e)
            {
                Console.WriteLine("\n" + e.Message);
            }

            return contentString;
        }

        /// <summary>
        /// Returns the contents of the specified file as a byte array.
        /// </summary>
        /// <param name="imageFilePath">The image file to read.</param>
        /// <returns>The byte array of the image data.</returns>
        private static byte[] GetImageAsByteArray(string imageFilePath)
        {
            using (FileStream fileStream =
                new FileStream(imageFilePath, FileMode.Open, FileAccess.Read))
            {
                BinaryReader binaryReader = new BinaryReader(fileStream);
                return binaryReader.ReadBytes((int)fileStream.Length);
            }
        }

        /// <summary>
        /// Formats the given JSON string by adding line breaks and indents.
        /// </summary>
        /// <param name="json">The raw JSON string to format.</param>
        /// <returns>The formatted JSON string.</returns>
        private static string JsonPrettyPrint(string json)
        {
            if (string.IsNullOrEmpty(json))
                return string.Empty;

            json = json.Replace(Environment.NewLine, "").Replace("\t", "");

            string INDENT_STRING = "    ";
            var indent = 0;
            var quoted = false;
            var sb = new StringBuilder();
            for (var i = 0; i < json.Length; i++)
            {
                var ch = json[i];
                switch (ch)
                {
                    case '{':
                    case '[':
                        sb.Append(ch);
                        if (!quoted)
                        {
                            sb.AppendLine();
                            Enumerable.Range(0, ++indent).ForEach(
                                item => sb.Append(INDENT_STRING));
                        }
                        break;
                    case '}':
                    case ']':
                        if (!quoted)
                        {
                            sb.AppendLine();
                            Enumerable.Range(0, --indent).ForEach(
                                item => sb.Append(INDENT_STRING));
                        }
                        sb.Append(ch);
                        break;
                    case '"':
                        sb.Append(ch);
                        bool escaped = false;
                        var index = i;
                        while (index > 0 && json[--index] == '\\')
                            escaped = !escaped;
                        if (!escaped)
                            quoted = !quoted;
                        break;
                    case ',':
                        sb.Append(ch);
                        if (!quoted)
                        {
                            sb.AppendLine();
                            Enumerable.Range(0, indent).ForEach(
                                item => sb.Append(INDENT_STRING));
                        }
                        break;
                    case ':':
                        sb.Append(ch);
                        if (!quoted)
                            sb.Append(" ");
                        break;
                    default:
                        sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }
    }

    static class Extensions
    {
        public static void ForEach<T>(this IEnumerable<T> ie, Action<T> action)
        {
            foreach (var i in ie)
            {
                action(i);
            }
        }
    }

    public static class EnumerableExtension
    {
        public static T PickRandom<T>(this IEnumerable<T> source)
        {
            return source.PickRandom(1).Single();
        }

        public static IEnumerable<T> PickRandom<T>(this IEnumerable<T> source, int count)
        {
            return source.Shuffle().Take(count);
        }

        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source)
        {
            return source.OrderBy(x => Guid.NewGuid());
        }
    }

    public class WebSocketMessage
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("messageType")]
        public int MessageType { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

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

    public class TextImageTemplate
    {
        public int LimitSize { get; set; }

        public string TemplateName { get; set; }

        public string Template { get; set; }

        public string ImageName { get; set; }
    }

    public class VisionAdultResult
    {
        [JsonProperty("adult")]
        public VisionAdultResultDetail Adult { get; set; }
    }

    public class VisionAdultResultDetail
    {
        [JsonProperty("isAdultContent")]
        public bool isAdultContent { get; set; }

        [JsonProperty("adultScore")]
        public double adultScore { get; set; }

        [JsonProperty("isRacyContent")]
        public bool isRacyContent { get; set; }

        [JsonProperty("racyScore")]
        public double racyScore { get; set; }
    }

    public class LineMessageEntity : TableEntity
    {
        public LineMessageEntity(string name, string id)
        {
            this.PartitionKey = name;
            this.RowKey = id;
        }

        public LineMessageEntity() { }

        public long Id { get; set; }

        public string Name { get; set; }

        public string Message { get; set; }
    }
}
