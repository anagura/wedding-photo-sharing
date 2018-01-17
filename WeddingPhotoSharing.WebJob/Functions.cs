using Microsoft.Azure.WebJobs;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
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
        const string path = "services/T8V3JEK71/B8TUKCULT/WJVwKFU1MtWsUgrVu901yBNh";

        public async static Task ProcessQueueMessage([QueueTrigger("line-bot-workitems")] string message, TextWriter log)
        {
            log.WriteLine(message);
            using (var content = new StringContent(message, Encoding.UTF8, "application/json"))
            {
                await httpClient.PostAsync(path, content);
            }
        }
    }
}
