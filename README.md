# Komado

旧称 MangaViewer。既存版の更新と設定の継続利用のため、実行ファイル名、インストール先、設定の保存先、AppUserModelID は MangaViewer のまま維持する。

ZIP・PDF と画像フォルダを読む Windows 向け漫画ビューワー。画面は [Figma](https://www.figma.com/design/x8IxqAYvCHo0D208eyaVmD/MangaViewer?node-id=10-23) のデザインに基づく。

## ダウンロード

最新の安定版へ更新された Windows 11（x64）に対応しています。

- [Microsoft Store版](https://apps.microsoft.com/detail/9N3GRRDSFRDZ)：無料。.NETランタイムを同梱し、更新はStoreが管理します。
- [インストーラー版（Komado-Setup.exe）](https://github.com/asonas/Komado/releases/latest/download/Komado-Setup.exe)：[.NET 10 Desktop Runtime（Windows x64）](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)が別途必要です。インストールには管理者権限が必要です。
- [リリースノート・過去のバージョン](https://github.com/asonas/Komado/releases)

インストーラー版はコード署名を付けていないため、Windowsが警告を表示する場合があります。

## 起動

ビルド済みの `artifacts/app/MangaViewer.exe` を起動する。最新の安定版へ更新された Windows 11 (64 bit) と .NET 10 LTS の Desktop Runtime が必要。プレビュー版は利用しない。

ランタイムが未導入なら、[公式ダウンロード](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) の「.NET Desktop Runtime」の Windows x64 をインストールする。開発には同ページの .NET SDK が必要で、Windows 版 SDK には Desktop Runtime も含まれる。10.0 系列の最新サービス更新を利用する。

ZIP・CBZ・PDF または画像フォルダをウィンドウへドロップする。読書中も別の作品をドロップできる。ZIP は解凍済みフォルダを作らずに読み込む。Ctrl + O では ZIP・CBZ・PDF を選択でき、実行ファイルへパスを引数で渡しても開ける。

PDF は文書のページ順で表示し、単ページ・見開き・自動表示、シーク、最近開いたファイルと表紙サムネイルに対応する。Windows 標準の PDF 描画機能を使い、追加の PDF 閲覧ソフトは不要。パスワードが必要な PDF には対応していない。

開始画面の「最近開いたファイル」には、正常に開いた ZIP・PDF・フォルダを新しい順に最大 10 件表示する。クリックで開き直せ、同じ作品を再度開くと先頭へ移動する。フルパスは項目にマウスを重ねると確認できる。履歴は `%LOCALAPPDATA%\MangaViewer\recent-files.json` に保存する。この機能の追加前には履歴を保存していないため、更新後に開いた作品から記録する。

最近開いた作品はウィンドウ左上から並べ、幅に合わせて折り返す。中央のアイコン・見出し・ドロップ案内は表示しない。ZIP・PDF・フォルダの 1 ページ目の中央を正方形に切り抜き、アプリがバックグラウンドで 256 × 256 ピクセルの PNG を生成して `%LOCALAPPDATA%\MangaViewer\Cache\Thumbnails` に保存する。再起動後はキャッシュを再利用し、生成方式や元の ZIP・PDF（フォルダの場合は表紙画像）のサイズ・更新日時が変わったら再生成する。表紙が読めない作品も項目を残し、本のアイコンで表示する。

対応画像は PNG、JPEG、BMP、GIF、TIFF。ファイル名の自然順で並べ、サブフォルダも読む。GIF・複数フレーム TIFF は最初のフレームを表示する。暗号化 ZIP と WebP・AVIF は対象外。

1 ページ目は表紙として中央に 1 枚で表示する。「自動」では画像の幅と高さを比べ、横長なら単独、縦長なら隣の縦長画像と見開きで表示する。縦長の次が横長なら、それぞれ単独にする。縦長だけの作品では表紙の次は 2・3 ページ目、次に 4・5 ページ目となる。正方形は縦長と同じ扱い。横長が混在しても、前後のページ送りで画像を飛ばさずに戻れる。

## 操作

| 入力 | 動作 |
| --- | --- |
| 左 1/3 をクリック、←、S、J | 左へ送る |
| 右 1/3 をクリック、→、F、L | 右へ送る |
| ホイール下／上 | 次／前 |
| 中央 1/3 をクリック | シークバーの表示／非表示 |
| シークバーをクリック・ドラッグ | 指定ページへ移動 |
| タイトルバーの「左で次へ」「右で次へ」 | クリック・キーの送り方向と見開きの配置を切り替え |
| 自動／単ページ／見開き | 自動判定または手動の配置を選ぶ。横長画像は見開きでも単独表示する |
| 全画面ボタン、F11 | 全画面を切り替え |
| Esc | メニュー・エラー・シークバーを閉じ、すべて閉じているときは全画面を解除 |

ページ送りキーは長押しで連続送りできる。Windows のキー入力の待ち時間とリピート速度に合わせ、画像表示が完了したタイミングで送る。キーを離すか別のウィンドウへ移ると停止する。

画像・ページ番号・シークバーは一緒に切り替わる。読み込み中は直前の表示位置を保ち、連続したシーク操作は最新の移動先へ追従する。通常のページ送りでは処理中の画像読み込みを取り消さず、デコード済みキャッシュを再利用する。

ページ送りは即時切り替え。画像の読み込みとデコードは UI スレッド外で行い、前後のページを先読みする。デコード済み画像は最大 8 ページ・約 192 MB を目安にキャッシュし、長辺の極端な画像は縮小して読み込む。

通常ウィンドウのサイズ、最大化状態、自動／単ページ／見開き、送り方向は終了時に `%LOCALAPPDATA%\MangaViewer\window.json` へ保存する。全画面で終了しても通常ウィンドウのサイズを保持する。

## インストールと更新

インストーラーは Inno Setup 6 で作成する。開発用 PC に [Inno Setup 6](https://jrsoftware.org/isdl.php) の安定版をインストールする（7 のプレビュー版は使わない）。アプリを使う PC には Inno Setup は不要。

```powershell
.\scripts\Build-Installer.ps1
```

生成される `artifacts/installer/Komado-Setup.exe` を実行すると、管理者の確認後に `C:\Program Files\MangaViewer` へインストールし、スタートメニューへ登録する。.NET 10 Desktop Runtime x64 は別途必要。この PC では SDK とともに導入済み。

スタートメニューから Komado を起動し、タスクバーのアイコンを右クリックして「タスクバーにピン留めする」を選ぶ。以前の `artifacts/app` から起動したアイコンをピン留めしていた場合は、先にそのピン留めを外す。

コードを更新したら、同じコマンドでインストーラーを作り直して実行する。アプリを閉じてから更新する。同じインストール先とアプリ ID を使うため、ピン留め先を変更せず更新版を起動できる。ビルドだけでは Program Files のアプリは更新されない。バージョンを指定する場合は `-Version 0.1.1` を付ける。同じバージョンの開発ビルドも上書き更新する。

アンインストールは Windows の「設定 → アプリ → インストールされているアプリ」から行う。終了時に「表紙サムネイルのキャッシュも削除しますか？」と確認し、「はい」を選ぶとサムネイルだけ削除する。元の作品、履歴とウィンドウ設定は削除しない。サイレントアンインストールではキャッシュを保持する。対象はアンインストーラーを実行するユーザーのデータ領域のみで、別の管理者アカウントで実行した場合は元のユーザーのキャッシュを自動削除しない。

## Microsoft Store 用 MSIX

Inno Setup 版とは別に、.NET 10 を同梱する x64 の MSIX を作成できる。ビルドには .NET 10 SDK と [Windows SDK](https://developer.microsoft.com/windows/downloads/windows-sdk/) の MakeAppx.exe が必要。SDK のインストールは手動で行う。

Partner Center でアプリ名を予約し、「製品 ID」に表示される Package/Identity/Name、Package/Identity/Publisher、Package/Properties/PublisherDisplayName を指定する。表示名を変更して予約した場合は `-DisplayName` も指定する。

```powershell
.\scripts\Build-StorePackage.ps1 `
    -IdentityName 'YUYAFujiwara.Komado' `
    -Publisher 'CN=3D989974-47D9-4C8D-B595-B7E1293B16CD' `
    -PublisherDisplayName 'YUYA Fujiwara' `
    -Version '1.0.0.0'
```

Publisher は `CN=` を含め、Partner Center の値全体をそのまま渡す。バージョンは 4 桁で各桁 0～65535（先頭は 1 以上）、末尾は Store 用に 0 とする。更新時には以前の提出より大きい値を指定する。MakeAppx は導入済み SDK の最新バージョンを自動検出する。必要なら `-MakeAppxPath` で実行ファイルを指定できる。

出力は `artifacts/store/Komado_1.0.0.0_x64.msix`。MakeAppx の検証を有効にして生成する。Store が署名するため、提出用パッケージは署名しない。このファイルを通常の方法で直接インストールするには別途署名と証明書の信頼設定が必要で、スクリプトはインストール・証明書登録・Store への提出を行わない。

Store 版は Windows がインストール先と更新を管理する。Inno Setup 版の固定パス・ショートカット・アンインストール時のキャッシュ削除確認は引き継がない。両者は別の配布として扱い、既存版から履歴や設定が自動移行されることは保証しない。公開前にはパッケージ環境でのドラッグ＆ドロップ、ZIP・PDF・フォルダの読み込み、履歴・設定の保存、更新を確認し、Windows App Certification Kit を実行する。

手順の根拠: [MSIX の手動作成](https://learn.microsoft.com/windows/msix/desktop/desktop-to-uwp-manual-conversion)、[Store のパッケージ要件](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/app-package-requirements)。

## 開発環境

.NET 10 SDK が必要。Windows API の参照パッケージはビルド時に NuGet から復元する。フォントは Noto Sans JP を優先し、未導入環境では Yu Gothic UI を使用する。

配布用スクリプトは NuGet のパッケージと HTTP キャッシュを `artifacts/` 内に保存し、終了時に環境変数を元に戻す。制限された実行環境で通常の HTTP キャッシュへ書き込めず NU1900 が出る場合、直接 `dotnet` を実行するときも `$env:NUGET_HTTP_CACHE_PATH = Join-Path $PWD 'artifacts/nuget-http-cache'` を設定し、`dotnet restore --force` にプロジェクトのパスを指定して復元し直す。脆弱性監査は無効にしない。

```powershell
dotnet build src/MangaViewer/MangaViewer.csproj -c Release
dotnet run --project tests/MangaViewer.Checks/MangaViewer.Checks.csproj -c Release
dotnet publish src/MangaViewer/MangaViewer.csproj -c Release -o artifacts/app
```

チェックプログラムはテスト画像・ZIP・PDF を生成し、実際の WPF ウィンドウで読み込み・キー操作・シーク・方向切り替え・サイズ保存を確認する。画像と測定結果は `artifacts/checks` に出力し、通常の設定ファイルは変更しない。

## ライセンス

Copyright 2026 YUYA Fujiwara

Komado は [Apache License 2.0](LICENSE)（`Apache-2.0`）で提供します。自作の UI アイコンも同ライセンスの対象です。Figma はこれらのアイコンの制作・書き出しに使用しています。

依存ライブラリやランタイムには、それぞれのライセンスが適用されます。
