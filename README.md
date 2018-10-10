# wedding-photo-sharing

LINEから投稿したメッセージや写真をウェブページに表示します。

関連プロジェクト：https://github.com/anagura/wedding-photo-sharing-receiver

## 事前準備

### LINE Developerアカウントの作成

LINE developersが必要になります。持ってない人はアカウントを作成してください。

https://developers.line.me/ja/

プロバイダーを作成し、新規チャネルでMessaging APIを選択します。
プランはDeveloper Trialとフリーどちらでも構いません。
人数が50人を超えそうならフリーにしておいたほうが良いでしょう。

### Azure アカウントの作成

Microsft Azureのアカウントも必要になります。こちらも持ってない人はアカウントを作成してください。

https://azure.microsoft.com/ja-jp/free/

### Slack ワークスペースの作成

ログの通知用にSlackのアカウントも必要になります。こちらも持ってない人はアカウントを作成してください。

https://slack.com/intl/ja-jp/


## LINEからのリクエストを受け付けるAzure Functionの設定

関連プロジェクトの
https://github.com/anagura/wedding-photo-sharing-receiver
をcloneして下さい。

### Azure Storageの作成

### Computer Visionの作成

### wedding-photo-sharing-receiverのデプロイ

### 環境変数の設定

#### ChannelSecret

#### LineAccessToken

#### StorageAccountName

#### StorageAccountKey

#### LineMediaContainerName

#### LineAdultMediaContainerName

#### LineMessageTableName

#### VisionSubscriptionKey

### LINE DevelopersでWebhook URLの設定

### 疎通確認

## 写真を表示するビューワーの設定

### WeddingPhotoViewerのデプロイ

## Azure Functionが生成したqueueを処理するWebJobの設定

### WebJobのデプロイ

### 環境変数の設定

#### StorageAccountName

#### StorageAccountKey

#### LineMediaContainerName

#### LineAdultMediaContainerName

#### SlackWebhookPath

#### WebsocketServerUrl


