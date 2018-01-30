namespace WeddingPhotoSharing.WebJob
{
    public class AppSettings : AppSettingsBase
    {
        protected AppSettings() { }

        public static string SlackWebhookPath => Setting("SlackWebhookPath");

        public static string WebsocketServerUrl => Setting("WebsocketServerUrl");
    }
}
