using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace WeddingPhotoViewer
{
    public class WebSocketHandler
    {
        private Dictionary<string, WebSocket> browsers = new Dictionary<string, WebSocket>();

        public async Task Photo(HttpContext context, WebSocket webSocket)
        {
            if (context.Request.Headers.TryGetValue("X-PhotoViewerId", out var id))
            {
                browsers[id] = webSocket;
                var cancellToken = new CancellationTokenSource(TimeSpan.FromMinutes(1));

                try
                {
                    var buffer = new byte[1024 * 4];
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellToken.Token);
                    while (!result.CloseStatus.HasValue)
                    {
                        // browser keep alive message
                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellToken.Token);
                    }
                    await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, cancellToken.Token);
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
        }

        public async Task Webjob(HttpContext context, WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            while (!result.CloseStatus.HasValue)
            {
                foreach (var browser in browsers)
                {
                    try
                    {
                        // send image url
                        await browser.Value.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, result.EndOfMessage, CancellationToken.None);
                    }
                    catch
                    {
                        // ignore
                    }
                }

                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            }
            await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
        }
    }
}
