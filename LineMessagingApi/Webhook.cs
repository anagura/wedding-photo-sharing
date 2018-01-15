using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace LineMessagingApi
{
    public class Webhook
    {
        private byte[] channelSecret;

        public Webhook(string channelSecret)
        {
            this.channelSecret = Encoding.UTF8.GetBytes(channelSecret);
        }

        public async Task<(bool valid, string content)> Verify(HttpRequestMessage req)
        {
            if (!req.Headers.TryGetValues("X-Line-Signature", out IEnumerable<string> headers))
            {
                return (false, (string)null);
            }

            var channelSignature = headers.FirstOrDefault();
            if (channelSignature == null)
            {
                return (false, (string)null);
            }

            var content = await req.Content.ReadAsStringAsync();
            var contentBytes = await req.Content.ReadAsByteArrayAsync();

            using (var hmacsha256 = new HMACSHA256(channelSecret))
            {
                var signature = Convert.ToBase64String(hmacsha256.ComputeHash(contentBytes));
                if (channelSignature != signature)
                {
                    return (false, (string)null);
                }
            }

            return (true, content);
        }
    }
}
