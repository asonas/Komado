# Komado 提出準備

Store ID: `9N3GRRDSFRDZ`
製品名: Komado
発行者表示名: YUYA Fujiwara
価格: 無料、全240市場、審査通過後すぐに公開（Partner Center設定済み）
プライバシーポリシー: `https://ason.as/komado/policy`（公開・登録済み）
パッケージ: `artifacts/store/Komado_1.0.0.0_x64.msix`

## 審査担当者向けメモ

This is a WPF desktop reader for local ZIP/CBZ archives, PDF files and image folders. No account or sign-in is required. Drag a supported file or image folder onto the window to open it. Ctrl+O opens the file picker. Use the mouse wheel or arrow keys to turn pages. Click the middle third of the reading area to show or hide the seek bar. Display layout and reading direction can be changed in the title bar.

The runFullTrust capability is required to run the packaged WPF desktop application and access files selected by the user. The app does not require administrator privileges. Reading material is supplied by the user; the app does not distribute or sell comics.

### DPI検査の補足（審査メモへの追記用）

Local WACK 10.0.26100.8249 reported a DPI warning because its BinaryInfo.IsPerMonitorV2DPIAwarenessSet call failed with E_FAIL (0x80004005) on the installed WindowsApps executable. The same WACK API returns true for a byte-identical copy outside WindowsApps. The installed executable contains the PerMonitorV2 manifest declaration, and GetWindowDpiAwarenessContext with AreDpiAwarenessContextsEqual confirmed that the running packaged app's main window is PerMonitorV2-aware. We retained the original WACK warning and did not alter the package to suppress it.

## 公開前の状態（2026-11-13）

- カテゴリ、IARC、個人情報申告「はい」、ポリシーURL、MSIX、日本語原稿、画像は登録済み。申請下書きの全項目は完了です。
- ZIP・CBZ・PDF・フォルダ、ページ送り、履歴・設定保存を実機確認済みです。
- WACKは22 PASS、任意検査1 FAIL、DPI検査1 WARNINGです。DPI警告はインストール先での検査処理の失敗として再現でき、同一バイナリの宣言判定と実行時のPerMonitorV2を別途確認しました。警告自体は残っています。
- アプリ側のDPI修正が必要な証拠はありません。不要なAPI呼び出し追加やWindowsAppsの権限変更は行わず、現行MSIXと調査結果で審査へ進める判断です。Microsoftの認定結果を保証するものではありません。
- 2026-11-13 15:17頃（日本時間）に審査へ提出済みです。DPI補足を含む内容で送信し、認定中・前処理中の表示を確認しました。認定通過後すぐに公開されます。

## 素材

スクリーンショットはアプリの WPF 描画をそのまま PNG に保存する。漫画部分のみ、掲載用に生成したオリジナルのサンプルを使用する。サンプル漫画は製品の同梱コンテンツではない。

生成方法: 内蔵 imagegen。プロンプト: 白黒・スクリーントーンの6コマ漫画。小さなロボットが海辺の町の書店で猫と出会い、本を読む。文字、ロゴ、既存キャラクターなし。全年齢向け。

公式の画像要件: https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/screenshots-and-images
