namespace WeddingPhotoSharing.WebJob
{
    public class AppSettings : AppSettingsBase
    {
        protected AppSettings() { }

        public static string SlackWebhookPath => Setting("SlackWebhookPath");

        public static string WebsocketServerUrl => Setting("WebsocketServerUrl");

		public static string LineAccessToken => Setting("LineAccessToken");

        public static string LineMediaContainerName => Setting("LineMediaContainerName");

        public static string StorageAccountName => Setting("StorageAccountName");

        public static string StorageAccountKey => Setting("StorageAccountKey");

        public static string VisionSubscriptionKey => Setting("VisionSubscriptionKey");
    }
}
