using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace WeddingPhotoViewer
{
    public class WebSocketHandler
    {
        private Dictionary<string, WebSocket> browsers = new Dictionary<string, WebSocket>();
        private ConcurrentQueue<WebSocketMessage> messageQueue = new ConcurrentQueue<WebSocketMessage>();

        public async Task Photo(HttpContext context, WebSocket webSocket)
        {
            var id = context.TraceIdentifier;
            browsers[id] = webSocket;

            try
            {
                // send queued message
                foreach (var msg in messageQueue)
                {
                    await webSocket.SendAsync(msg.Message, msg.MessageType, msg.EndOfMessage, CancellationToken.None);
                }

                var buffer = new byte[1024 * 4];
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                while (!result.CloseStatus.HasValue)
                {
                    // browser keep alive message
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                }
                await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine("{0} is disconnected.{1}", id, ex);
            }
            browsers.Remove(id);
        }

        public async Task Webjob(HttpContext context, WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            while (!result.CloseStatus.HasValue)
            {
                // 内部キューに保存
                var sendMessage = new ArraySegment<byte>(buffer, 0, result.Count);

                var oneMsg = new WebSocketMessage()
                {
                    EndOfMessage = result.EndOfMessage,
                    MessageType = result.MessageType,
                };
                var queueBuffer = new byte[1024 * 4];
                sendMessage.CopyTo(queueBuffer);
                oneMsg.Message = new ArraySegment<byte>(queueBuffer, 0, result.Count);
                messageQueue.Enqueue(oneMsg);

                if (messageQueue.Count() > 10) {
                    if (messageQueue.TryDequeue(out WebSocketMessage msg))
                    {
                        Console.WriteLine(msg.ToString());
                    }
                }

                List<string> deleteList = new List<string>();
                foreach (var browser in browsers)
                {
                    try
                    {
                        if (browser.Value.State == WebSocketState.Open)
                        {
                            // send image url
                            await browser.Value.SendAsync(sendMessage, result.MessageType, result.EndOfMessage, CancellationToken.None);
                        } else
                        {
                            deleteList.Add(browser.Key);
                        }
                    }
                    catch (Exception ex)
                    {
                        // ignore
                        deleteList.Add(browser.Key);
                    }
                }
                if (deleteList.Any())
                {
                    deleteList.ForEach(x =>
                   {
                       browsers.Remove(x);
                   });
                }
                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            }
            await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
        }
    }

    public class WebSocketMessage {
        public ArraySegment<byte> Message { get; set; }
        public bool EndOfMessage { get; set; }
        public WebSocketMessageType MessageType { get; set; }
    }
}
