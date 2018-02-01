using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace WeddingPhotoSharing.WebJob
{
    public static class TaskExtensions
    {
        public static void FireAndForget(this Task task, TextWriter log = null)
        {
            task.ConfigureAwait(false);
            task.ContinueWith(x =>
            {
                if (x.Exception != null)
                {
                    log?.WriteLine("TaskUnhandled: " + x.Exception.ToString());
                }
                else
                {
                    log?.WriteLine("TaskUnhandled");
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        public static Task WhenAll(this IEnumerable<Task> tasks)
        {
            return Task.WhenAll(tasks);
        }

        public static Task<T[]> WhenAll<T>(this IEnumerable<Task<T>> tasks)
        {
            return Task.WhenAll(tasks);
        }
    }
}
