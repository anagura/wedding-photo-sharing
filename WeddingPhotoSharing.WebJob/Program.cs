using Microsoft.Azure.WebJobs;

namespace WeddingPhotoSharing.WebJob
{
    class Program
    {
        static void Main(string[] args)
        {
            var host = new JobHost();
            host.RunAndBlock();
        }
    }
}
