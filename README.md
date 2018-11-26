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

### wedding-photo-sharing-receiverのデプロイ

Visual StudioからAzureでデプロイします。
コンソールからFunction Appが作成されているのを確認します。

### Azure Storageの作成

Azureコンソールから「ストレージアカウント」→「追加」を選びストレージアカウントを作成します。

#### Azure StorageにLINEの画像を格納するコンテナの作成

作成したストレージアカウントを選択し、概要の「サービス」→「BLOB」→「＋コンテナー」からLINE画像保存用のコンテナを作成する。
パブリックアクセスレベルは「コンテナー」にしておく。

#### Azure StorageにLINEのNG画像を格納するコンテナの作成

同じく作成したストレージアカウントを選択し、概要の「サービス」→「BLOB」→「＋コンテナー」からNG判定となったLINE画像保存用のコンテナを作成する。
パブリックアクセスレベルは「コンテナー」にしておく。

#### Azure StorageにLINEの生メッセージを格納するテーブルの作成

作成したストレージアカウントを選択し、概要の「サービス」→「テーブル」→「＋テーブル」からLINEの生メッセージ保存用のコンテナを作成する。

#### Azure Storageに完成メッセージ情報を格納するコンテナの作成

作成したストレージアカウントを選択し、概要の「サービス」→「テーブル」→「＋テーブル」から完成メッセージ保存用のコンテナを作成する。


### Computer Visionの作成

Azureコンソールの「リソースの作成」を選択し、検索窓に「computer vision」と入れて検索します。
「Computer Vision API」が見つかったら作成します。


### 環境変数の設定

作成したFunction Appの「アプリケーション設定」にある「アプリ設定名」に下記環境変数を追加していきます。

#### ChannelSecret

LINE Developersで作成したチャネルのページからChannnelSecretをコピーして設定します。

#### LineAccessToken

同じくLINE Developersで作成したチャネルのページからAccessTokenをコピーして設定します。

#### StorageAccountName

AzureStorageの名前を設定します。

#### StorageAccountKey
AzureStorageの「アクセスキー」にあるキーを設定します。

#### LineMediaContainerName

Azure Storageに作成したLINE画像保存用コンテナの名前を指定する

#### LineAdultMediaContainerName

Azure Storageに作成したNG判定されたLINE画像保存用コンテナの名前を指定する

#### LineMessageTableName

Azure Storageに作成したLINEの生メッセージ保存用テーブルの名前を指定する

#### VisionSubscriptionKey

作成したComputer VisionのKeysからキーを貼り付けます。

### LINE DevelopersでWebhook URLの設定

FunctionAppの「関数URLの取得」でURLを取得したら、LINE Developersで作成したチャネルのWebHook Urlに設定します。

### 疎通確認

LINE developersで作成したチャンネルをLINEで友達登録し、メッセージか画像を送信する。
AzureStorageのコンテナかテーブルに送信したメッセージや画像が登録されるのを確認。

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


