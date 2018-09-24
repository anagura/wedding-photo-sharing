using ImageGeneration;
using LineMessaging;
using Microsoft.Azure.WebJobs;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
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

		private static readonly string SlackWebhookPath = AppSettings.SlackWebhookPath;
		private static readonly string WebsocketServerUrl = AppSettings.WebsocketServerUrl;
		private static readonly string LineMediaContainerName = AppSettings.LineMediaContainerName;
		private static readonly string LineAdultMediaContainerName = AppSettings.LineAdultMediaContainerName;
		private static readonly string LineMessageTableName = AppSettings.LineMessageTableName;
		private static readonly string StorageAccountName = AppSettings.StorageAccountName;
		private static readonly string StorageAccountKey = AppSettings.StorageAccountKey;
		private static readonly string VisionSubscriptionKey = AppSettings.VisionSubscriptionKey;

		private static ClientWebSocket webSocket;
		private static readonly ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();

		// Azure Storage
		private static readonly StorageCredentials storageCredentials;
		private static readonly CloudStorageAccount storageAccount;

		// 画像格納のBlob
		private static readonly CloudBlobClient blobClient;
		private static readonly CloudBlobContainer container;
		private static readonly CloudBlobContainer adultContainer;

		// メッセージ格納のTable
		private static readonly CloudTableClient tableClient;
		private static readonly CloudTable table;

		private const int ImageHeight = 300;
		private const int MessageLength = 40;

		private static readonly string TemplateDirectoryName = "TextTemplates";
		private static List<TextImageTemplate> _imageTemplates = new List<TextImageTemplate>();


		static Functions()
		{
			webSocket = new ClientWebSocket();
			webSocket.Options.KeepAliveInterval = TimeSpan.FromMinutes(1);
			TryConnect(Console.Out);

			storageCredentials = new StorageCredentials(StorageAccountName, StorageAccountKey);
			storageAccount = new CloudStorageAccount(storageCredentials, true);
			blobClient = storageAccount.CreateCloudBlobClient();
			container = blobClient.GetContainerReference(LineMediaContainerName);
			adultContainer = blobClient.GetContainerReference(LineAdultMediaContainerName);

			tableClient = storageAccount.CreateCloudTableClient();
			table = tableClient.GetTableReference(LineMessageTableName);
			table.CreateIfNotExists();

			_imageTemplates.Add(new TextImageTemplate { LimitSize = 8, ImageName = "12071.png", TemplateName = "Big8.xaml" });
			_imageTemplates.Add(new TextImageTemplate { LimitSize = 20, ImageName = "51881.png", TemplateName = "51881.xaml" });
			_imageTemplates.Add(new TextImageTemplate { LimitSize = 30, ImageName = "51893.png", TemplateName = "Middle10.xaml" });
			_imageTemplates.Add(new TextImageTemplate { LimitSize = 30, ImageName = "29831.png", TemplateName = "29831.xaml" });
			_imageTemplates.Add(new TextImageTemplate { LimitSize = MessageLength, ImageName = "82096.png", TemplateName = "TextPanel.xaml" });

			_imageTemplates.ForEach(x =>
			{
				x.Template = File.ReadAllText(TemplateDirectoryName + "/" + x.TemplateName);
			});
		}

		public async static Task ProcessQueueMessage([QueueTrigger("line-bot-workitems")] string message, TextWriter log)
		{
			if (string.IsNullOrEmpty(message))
			{
				return;
			}

			if (webSocket.State != WebSocketState.Open
				&& webSocket.State != WebSocketState.Connecting)
			{
				log.WriteLine("websocket state:" + webSocket.State);
				TryConnect(log);

				// send at next chance
				messageQueue.Enqueue(message);
				return;
			}

			try
			{
				if (messageQueue.Count > 0)
				{
					while (messageQueue.TryDequeue(out string oldMessage))
					{
						log.WriteLine("old message: " + oldMessage);
						oldMessage = await GetContentFromLine(oldMessage, log);
						if (!string.IsNullOrEmpty(oldMessage))
						{
							//                          await PostToSlack(oldMessage, log);
							await PostToWebsocket(oldMessage, log);
						}
					}
				}

				message = await GetContentFromLine(message, log);
				if (!string.IsNullOrEmpty(message))
				{
					//                    await PostToSlack(message, log);
					await PostToWebsocket(message, log);
				}
			}
			catch (Exception ex)
			{
				log.WriteLine(ex.ToString());
				await PostToSlack(ex.ToString(), log);
				throw ex;   // 次回再送するため
			}
		}

		private static async Task<string> GetContentFromLine(string message, TextWriter log)
		{
			List<WebSocketMessage> lineMessages = new List<WebSocketMessage>();
			LineResult[] content = JsonConvert.DeserializeObject<LineResult[]>(message);
			foreach (LineResult eventMessage in content)
			{
				WebSocketMessage result = new WebSocketMessage
				{
					Name = eventMessage.Name,
					MessageType = eventMessage.MessageType
				};
				var fileName = eventMessage.Id.ToString() + ".jpg";

				if (eventMessage.MessageType == (int)MessageType.Text)
				{
					result.Message = eventMessage.Message;

					// テキストを画像化
					string textMessage = eventMessage.Message;
					var textLength = textMessage.Length > MessageLength ? MessageLength : textMessage.Length;
					var template = _imageTemplates.Where(x => x.LimitSize >= textLength).PickRandom();

					dynamic viewModel = new ExpandoObject();
					viewModel.Name = result.Name;
					viewModel.Text = textMessage;
					viewModel.Source = string.Format("{0}\\{1}\\{2}", Directory.GetCurrentDirectory(), TemplateDirectoryName, template.ImageName);

					byte[] image = ImageGenerator2.GenerateImage(template.Template, viewModel, template.ImageName);

					// 画像をストレージにアップロード
					await UploadImageToStorage(fileName, image);
					result.ImageUrl = result.ThumbnailImageUrl = GetUrl(fileName);
				}
				else if (eventMessage.MessageType == (int)MessageType.Image)
				{
					// LINEから画像を取得
					var lineResult = DownloadImageFromStorage(fileName);

					result.ImageUrl = eventMessage.ImageUrl;

					// サムネイル
					var thumbnailFileName = string.Format("thumbnail_{0}{1}", eventMessage.Id, ".jpg");
					await ResizeUpload(thumbnailFileName, lineResult, ImageHeight);
					result.ThumbnailImageUrl = GetUrl(thumbnailFileName);
				}
				else
				{
					log.WriteLine("not supported message type:" + eventMessage.MessageType);
					continue;
				}

				lineMessages.Add(result);
			}

			return lineMessages.Any() ? JsonConvert.SerializeObject(lineMessages) : string.Empty;
		}

		private static async Task ResizeUpload(string fileName, byte[] sourceImage, int height)
		{
			using (Image<Rgba32> image = SixLabors.ImageSharp.Image.Load(sourceImage))
			{
				if (image.Height > height)
				{
					int ratio = height / image.Height;
					image.Mutate(x => x
								 .Resize(image.Width * ratio, height));
				}

				using (var ms = new MemoryStream())
				{
					image.SaveAsJpeg(ms);
					ms.Position = 0;    //Move the pointer to the start of stream.
					await UploadStreamImageToStorage(fileName, ms);
				}
			}
		}

		private static async Task UploadStreamImageToStorage(string fileName, Stream stream)
		{
			CloudBlockBlob blockBlob = container.GetBlockBlobReference(fileName);
			blockBlob.Properties.ContentType = "image/jpeg";

			await blockBlob.UploadFromStreamAsync(stream);
		}

		private static byte[] DownloadImageFromStorage(string fileName)
		{
			//ダウンロードするファイル名を指定
			CloudBlockBlob cloudBlockBlob = container.GetBlockBlobReference(fileName);

			//ダウンロード処理
			//ダウンロード後のパスとファイル名を指定。
			var cloudBlobStream = cloudBlockBlob.OpenReadAsync().Result;

			using (MemoryStream ms = new MemoryStream())
			{
				cloudBlobStream.CopyTo(ms);
				return ms.ToArray();
			}
		}

		private static async Task UploadImageToStorage(string fileName, byte[] image, bool isAdult = false)
		{
			CloudBlockBlob blockBlob = isAdult ?
				adultContainer.GetBlockBlobReference(fileName) :
				container.GetBlockBlobReference(fileName);

			blockBlob.Properties.ContentType = "image/jpeg";

			await blockBlob.UploadFromByteArrayAsync(image, 0, image.Length);
		}

		private static string GetUrl(string fileName, bool isAdult = false)
		{
			return string.Format("https://{0}.blob.core.windows.net/{1}/{2}", StorageAccountName, isAdult ? LineAdultMediaContainerName : LineMediaContainerName, fileName);
		}

		private static void TryConnect(TextWriter log)
		{
			try
			{
				// set timeout
				webSocket = new ClientWebSocket();
				webSocket.Options.KeepAliveInterval = TimeSpan.FromMinutes(1);

				var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
				webSocket.ConnectAsync(new Uri(WebsocketServerUrl), cancellationTokenSource.Token).FireAndForget(log);
			}
			catch (Exception ex)
			{
				log.WriteLine("ConnectAndForget: " + ex.ToString());
			}
		}

		private static async Task PostToSlack(string message, TextWriter log)
		{
			try
			{
				var slackMessage = new SlackMessage
				{
					Text = $"```{message}```"
				};
				var json = JsonConvert.SerializeObject(slackMessage);
				using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
				{
					await httpClient.PostAsync(SlackWebhookPath, content);
				}
			}
			catch (Exception ex)
			{
				log.WriteLine("PostToSlack: " + ex.ToString());
			}
		}

		private static async Task PostToWebsocket(string message, TextWriter log)
		{
			try
			{
				var messageBytes = Encoding.UTF8.GetBytes(message);

				// set timeout
				var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));

				const int bufferSize = 1024 * 4;
				var list = new List<Task>();
				foreach (var (buffer, index) in messageBytes.Buffer(bufferSize).Indexed())
				{
					var chunk = buffer.ToArray();
					var endOfMessage = ((double)messageBytes.Length / bufferSize).Floor() == index;

					if (webSocket.State != WebSocketState.Open
						&& webSocket.State != WebSocketState.Connecting)
					{
						TryConnect(log);
					}

					await webSocket.SendAsync(new ArraySegment<byte>(chunk, 0, chunk.Length), WebSocketMessageType.Text, endOfMessage, cancellationTokenSource.Token);
				}
			}
			catch (Exception ex)
			{
				log.WriteLine("PostToWebsocket: " + ex.ToString());
				throw ex;
			}
		}
	}

	public static class EnumerableExtension
	{
		public static T PickRandom<T>(this IEnumerable<T> source)
		{
			return source.PickRandom(1).Single();
		}

		public static IEnumerable<T> PickRandom<T>(this IEnumerable<T> source, int count)
		{
			return source.Shuffle().Take(count);
		}

		public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source)
		{
			return source.OrderBy(x => Guid.NewGuid());
		}
	}

	public class WebSocketMessage
	{
		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("messageType")]
		public int MessageType { get; set; }

		[JsonProperty("message")]
		public string Message { get; set; }

		[JsonProperty("imageUrl")]
		public string ImageUrl { get; set; }

		[JsonProperty("thumbnailImageUrl")]
		public string ThumbnailImageUrl { get; set; }
	}

	public class SlackMessage
	{
		[JsonProperty("text")]
		public string Text { get; set; }
	}

	public class TextImageTemplate
	{
		public int LimitSize { get; set; }

		public string TemplateName { get; set; }

		public string Template { get; set; }

		public string ImageName { get; set; }
	}

}

public class LineResult
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("messageType")]
	public int MessageType { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("imageUrl")]
	public string ImageUrl { get; set; }
}

