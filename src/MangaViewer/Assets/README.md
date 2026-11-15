# UI assets

YUYA Fujiwara が自作した UI アイコン。Komado と同じ Apache License 2.0 を適用する。Figma の `MangaViewer` ファイル `x8IxqAYvCHo0D208eyaVmD` のノード `10:23` から書き出し、SVG を元寸法のまま 3 倍密度の PNG へ変換して WPF の Resource に含めている。アプリは外部 URL にアクセスしない。

| 名前 | 元アセット ID |
| --- | --- |
| book | 5acf3 |
| down | ebeb3 |
| fullscreen | de414 |
| minimize | 39bcc |
| maximize | a3a97 |
| close | 9c502 |
| grid | dd772 |

漫画のサンプル絵コンテはアプリに含めず、ユーザーが開いた画像を表示する。

## アプリアイコン

[Figma の採用案 B「見開き」](https://www.figma.com/design/x8IxqAYvCHo0D208eyaVmD/MangaViewer?node-id=19-79) を使用する。`app-icon.svg` は編集用原稿、`app-icon-{size}.png` は Figma から取得した透明背景の書き出し。16・24 px は小サイズ専用図案で、32・48・256 px は詳細図案を使用する。

`app-icon.ico` は上記 5 サイズの PNG を格納した複数解像度のアイコン。実行ファイル、ウィンドウ、タイトルバーに設定する。
