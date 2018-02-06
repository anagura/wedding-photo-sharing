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

        public async Task Photo(HttpContext context, WebSocket webSocket)
        {
            var id = context.TraceIdentifier;
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

        public async Task Webjob(HttpContext context, WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            while (!result.CloseStatus.HasValue)
            {
                List<string> deleteList = new List<string>();
                foreach (var browser in browsers)
                {
                    try
                    {
                        if (browser.Value.State == WebSocketState.Open)
                        {
                            // send image url
                            await browser.Value.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, result.EndOfMessage, CancellationToken.None);
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
}
