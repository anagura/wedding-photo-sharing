using Microsoft.Azure.WebJobs;
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
        private static readonly ClientWebSocket webSocket;
        private static readonly ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();

        static Functions()
        {
            webSocket = new ClientWebSocket();
            webSocket.Options.KeepAliveInterval = TimeSpan.FromMinutes(1);
            TryConnect(Console.Out);
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
            await PostToSlack(message, log);
            await PostToWebsocket(message, log);
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

    public class SlackMessage
    {
        [JsonProperty("text")]
        public string Text { get; set; }
    }
}
