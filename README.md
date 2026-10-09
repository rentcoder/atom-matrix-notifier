# ATOM Matrix Notifier

ChatGPT・Codex・DotのWindows通知を、M5Stack ATOM Matrixの光で知らせるガジェットです。
Windowsの通知ブリッジとATOMはBluetooth Classic SPP/RFCOMMで通信します。Wi-Fi設定やOpenAI APIキーは不要です。

## 対応環境

- M5Stack ATOM Matrix（ESP32-PICO-D4、5×5 LED）。Bluetooth ClassicのないATOM S3などには非対応。
- Bluetooth Classic搭載Windows 10 2004以降／Windows 11、x64。
- Arduino IDE、M5Stack ESP32ボードパッケージ **3.3.9**、Adafruit NeoPixel **1.15.5**。
- Windowsブリッジのビルドには **.NET 8 SDK**。

## 省電力動作

- 待機中はLED消灯。Bluetoothを終了して**20秒間Deep Sleep**。
- タイマーで起床し、PC側が保持した通知を受信して再び眠ります。
- 新着通知を約**4秒**表示。明るさは8/255。複数種類は1秒ごとに交代表示。
- CPUは80MHz。起動中はBluetooth Modem Sleepを有効化し、使わないBLEは無効化。
- PCと接続できない場合は約10秒受信待ちしてから20秒眠ります。
- 未確認状態はRTCメモリーでスリープをまたいで保持。電源断では消えます。

通知転送の遅延は通常20〜30秒程度を目標にしています。Windowsの再接続や電波状況により長くなるため、30秒以内を厳密には保証しません。Deep Sleep中はBluetooth受信できません。

消費電流・電池持ち・温度は未測定です。バッテリーベース、電源回路、USB変換器、消灯中のLED回路にも待機電流が残ります。

## ATOMへの書き込み

1. Arduino IDEの追加ボードマネージャURLに次を登録します。

   ```text
   https://static-cdn.m5stack.com/resource/arduino/package_m5stack_index.json
   ```

2. M5Stackボードパッケージ3.3.9とAdafruit NeoPixel 1.15.5をインストールします。
3. [AtomNotifier.ino](firmware/AtomNotifier/AtomNotifier.ino) を開きます。
4. データ対応USBケーブルで接続し、ボード **M5Atom**、書き込み速度 **115200**、USBのCOMポートを選んで書き込みます。

LEDはGPIO27、ボタンはGPIO39です。USBは給電と更新用で、通知の通信には使いません。

## Windowsブリッジのビルドと導入

リポジトリを固定のフォルダーに配置し、PowerShellで実行します。

```powershell
.\Build.ps1
```

生成される `app/` は.NETランタイムを含む自己完結版です。ビルド済みアプリはGit管理の対象外です。

1. Windowsの開発者モードを有効にします。未署名の自作アプリの登録に必要です。
2. `Install.cmd` を実行して現在のユーザーに登録します。管理者としての実行は不要です。
3. WindowsのBluetooth設定から **ATOM-Notify** をペアリングします。
4. `Start.cmd` で起動し、通知アクセスの確認を許可します。
5. 通知領域のアイコンをダブルクリックすると設定を開けます。「省電力監視中」で動作します。

起動直後は設定画面を隠して通知領域に常駐します。設定画面の×や最小化では終了しません。終了はアイコンの右クリック→「終了」です。更新・再ビルドの前にも終了してください。

右クリック→「通知表示テスト」で3種類を送れます。ATOMが眠っていれば次の起床まで待ちます。Bluetoothが普段切断されているのは正常です。

Windows起動時に自動起動するには、`Win+R` → `shell:startup` を開き、固定フォルダー内の `Start.cmd` へのショートカットを置きます。登録後は `app/` の場所を移動しないでください。

## 表示とボタン

| 種類 | 表示 |
| --- | --- |
| Chat | 緑の吹き出し |
| Codex | 青の `<>` |
| Dot / Dots | 紫の点 |

- スリープ中の短押し：起床し、未確認通知を約4秒再表示。
- 表示中に再度短押し：表示中の種類を確認済みに。
- 1.5秒長押し：全種類を確認済みに。

同じ種類の通知はまとめます。Windows通知そのものは削除しません。

## バッジによる保持の消去（v1.5）

通知を受け取るきっかけは**Windows通知センターの新着通知**です。保持を消す判断には、**ChatGPT／Codexのタスクバーアイコンに付く未読バッジの有無**を使います。

- バッジあり：ATOMの未確認状態を保持します。前面にしただけでは消しません。
- バッジなし：連続3回・2秒以上の確認後、Chat・Codex・Dotsの保持をすべて消します。通常の2秒間隔の監視では約4秒で確定します。
- 読み取り失敗、ロック画面、アイコン非表示、対象アプリ未起動、複数対象で判別不能：自動消去を保留します。

バッジの内訳は使いません。Windows通知センターから通知を削除しても、その理由だけでATOMの保持や送信待ちを消しません。新着通知から3秒間は、バッジの表示が遅れる場合に備えて自動消去を待ちます。バッジが消えた時点で送信待ちの通知も確認済みにします。既存のWindows通知が残っていても、それだけで再通知しません。

ATOMが眠っている場合、消去指示をPCで保持し、次の起床時に反映します。`CLEAR 7` で3種類をまとめて消します。ATOMのボタンで確認済みにした状態も、既存通知から勝手に復活しません。

バッジはExplorerが公開するアクセシビリティ説明（例：`未読のお知らせ 1 件`）から読み取ります。画像認識やChatGPT内部の既読APIは使用しません。対象アプリを自動選択し、状態を設定画面に表示します。このPCでは統合アプリの `OpenAI.Codex` タスクバーボタンを確認しています。

初めて使う環境では、対象アプリのバッジを一度実際に取得するまで、空の説明を「バッジなし」と断定しません。読み取り実績はアプリデータ領域の `badge-reader.json` に保存します。Windowsのタスクバーバッジ設定を無効にしている場合も自動消去しません。

ChatGPTとCodexなど対象が複数あり選べない場合は、ブリッジ終了中にアプリデータ領域の `settings.json` の `BadgeAppId` へ対象のAppUserModelIDを指定できます。空欄は自動選択です。ブラウザー版のタブ内やアドレスバー内のバッジは対象外です。

トレイの「Chatを確認済みにする」「すべて確認済みにする」とATOMのボタンは、明示的な確認操作として引き続き利用できます。Windows通知そのものは削除しません。

v1.4のファームウェアを使用中なら、v1.5への更新はWindowsブリッジだけで対応できます。それ以前のファームウェアではATOMも更新してください。プロトコルは `CLEAR <mask>`（Chat=1、Codex=2、Dots=4）です。

## 通知の分類とデータ

Windows通知アクセスで取得できる通知を分類します。ChatGPTやCodexからWindowsへ届かないイベントは取得できません。

初期設定は対象アプリ名 `ChatGPT|Codex`、Dots識別語 `\bdots?\b|ドット`、Codex識別語 `\bcodex\b|コーデックス` です。対象アプリ名に一致する新着通知について、アプリ名・タイトル・本文からDots→Codex→Chatの優先順で分類します。

同じアプリ名で届き、内容に識別語がない場合はChatに分類されます。必要に応じて設定画面で実際の通知に出る語を追加してください。正規表現を使用し、大文字・小文字は区別しません。保存後の再接続で設定を反映します。

通知本文はPC内の分類にだけ使用し、ATOMへ送るのは種類だけです。通知本文をファイルへ保存したり、インターネットへ送信する機能はありません。分類確認用の画面ログは任意です。

PC側の未送信キューはメモリー上で保持します。ブリッジを終了すると消えます。起動前の既存通知は転送しません。取得前に消した通知や、Windowsが同じID・作成時刻のまま更新した通知は検出できない場合があります。PCの休止中は転送できません。

設定と動作診断はWindowsのアプリデータ領域に保存します。診断には送信数、CPU周波数、機器の復帰回数などが含まれ、通知タイトルや本文は含まれません。

## 検証

実機ESP32-PICO-D4への書き込みとBluetooth通信、CPU80MHz・Modem Sleep有効、Deep Sleep復帰回数の増加、スリープをまたぐ3種類の未確認状態保持、PC側キューからの3種類の転送・受領確認を検証しました。

v1.5では実際の対象アプリのバッジありの読取と、バッジありの間にATOMの保持を維持する通信を確認しました。検証用アプリを使い、実際のExplorerでバッジ表示→消失の読取と読取実績の再読み込みも確認しています。実際の対象バッジが自然に消えたあと、消去命令が送られATOMの保持がPENDING2からPENDING0になったことも確認しました。検証のためにChatGPTの画面や通知を操作してはいません。

物理LEDの見え方、ボタンによる復帰・確認操作、個別の実際のChat・Codex・Dot通知はこの検証では直接観察していません。

通知同期ロジックは以下で検証できます（追加のテストライブラリーは不要）。

```powershell
dotnet run --project tests/NotificationState.Tests -c Release
```

通知センターから消えても保持すること、バッジの有無・不明状態、日本語／英語の説明、連続確認、新着通知の猶予、消去応答中の競合など25項目を検証します。

Windowsの通常のデスクトップで、ChatGPT／Codexアプリ起動中なら、次のExplorer統合テストも実行できます。最小化した検証用ウィンドウのバッジだけを一時的に表示・消去し、自動で終了します。ユーザーのChatGPT通知には触れません。

```powershell
dotnet run --project tests/TaskbarBadge.Integration -c Release
```

## 構成

- `firmware/AtomNotifier/`：Arduinoファームウェア。
- `windows/`：.NET 8 WPF／Windows Forms通知領域アイコン、Windows通知リスナー、RFCOMM通信。
- `Build.ps1`：Windowsアプリの自己完結ビルド。
- `Install.cmd` / `Install.ps1`：現在のユーザーへ登録。
- `Start.cmd`：登録済みアプリを起動。
- `Uninstall.ps1`：アプリ登録を解除。Windowsがアプリデータを削除する場合があります。

OpenAI・M5Stackの公式製品ではありません。

## 参考資料

- [Espressif Sleep Modes](https://docs.espressif.com/projects/esp-idf/en/v5.5.2/esp32/api-reference/system/sleep_modes.html)
- [Espressif BluetoothSerial](https://docs.espressif.com/projects/arduino-esp32/en/latest/api/bluetooth.html)
- [Microsoft Bluetooth RFCOMM](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/send-or-receive-files-with-rfcomm)
- [Microsoft Notification Listener](https://learn.microsoft.com/en-us/windows/uwp/design/shell/tiles-and-notifications/notification-listener)
- [M5Stack ATOMの書き込み](https://docs.m5stack.com/en/arduino/m5atom/program)

- [Microsoft SetOverlayIcon（アクセシビリティ説明）](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-itaskbarlist3-setoverlayicon)
