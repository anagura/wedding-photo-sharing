using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace WeddingPhotoViewer
{
    public class WebSocketHandler
    {
        private Dictionary<string, WebSocket> browsers = new Dictionary<string, WebSocket>();
        private List<WebSocketMessage> messageList = new List<WebSocketMessage>();

        public async Task Photo(HttpContext context, WebSocket webSocket)
        {
            var id = context.TraceIdentifier;
            browsers[id] = webSocket;

            try
            {
                // send queued message
                foreach (var msg in messageList)
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
                browsers.Remove(id);
            }
            catch (TaskCanceledException)
            {
                browsers.Remove(id);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        public async Task Webjob(HttpContext context, WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            while (!result.CloseStatus.HasValue)
            {
                // 内部キューに保存
                var sendMessage = new ArraySegment<byte>(buffer, 0, result.Count);
                messageList.Add(new WebSocketMessage(){
                    EndOfMessage = result.EndOfMessage,
                    MessageType = result.MessageType,
                    Message = sendMessage
                });

                if (messageList.Count() > 10) {
                    messageList.Remove(messageList.FirstOrDefault());
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
                        Console.WriteLine(ex);
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
