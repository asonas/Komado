using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MangaViewer;

public sealed class ThumbnailCache(string directory)
{
    private const int Size = 256;
    private readonly SemaphoreSlim gate = new(1);

    public async Task<BitmapSource> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run<BitmapSource>(async () =>
            {
                string fullPath = Path.GetFullPath(path);
                BookDocument? book = null;
                string source = fullPath;
                if (Directory.Exists(fullPath))
                {
                    book = BookDocument.Open(fullPath);
                    source = Path.Combine(fullPath, book.PageName(0));
                }
                var info = new FileInfo(source);
                string identity = $"v2|{fullPath.ToUpperInvariant()}|{source}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
                string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
                string cachedPath = Path.Combine(directory, key + ".png");
                if (File.Exists(cachedPath))
                {
                    try
                    {
                        using var cached = File.OpenRead(cachedPath);
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = cached;
                        image.EndInit();
                        image.Freeze();
                        if (image.PixelWidth == Size && image.PixelHeight == Size) return image;
                    }
                    catch (Exception exception) when (exception is IOException or NotSupportedException or FileFormatException)
                    {
                        // 再生成できるキャッシュなので、壊れていた場合は元の表紙を読み直す。
                    }
                }

                book ??= BookDocument.Open(fullPath);
                var cover = await book.GetPageAsync(0, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                double scale = Math.Max((double)Size / cover.PixelWidth, (double)Size / cover.PixelHeight);
                double width = cover.PixelWidth * scale;
                double height = cover.PixelHeight * scale;
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(28, 31, 34)), null, new Rect(0, 0, Size, Size));
                    drawing.PushClip(new RectangleGeometry(new Rect(0, 0, Size, Size)));
                    drawing.DrawImage(cover, new Rect((Size - width) / 2, (Size - height) / 2, width, height));
                    drawing.Pop();
                }
                var thumbnail = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
                thumbnail.Render(visual);
                thumbnail.Freeze();
                try
                {
                    Directory.CreateDirectory(directory);
                    string temporary = cachedPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(thumbnail));
                        using (var file = File.Create(temporary)) encoder.Save(file);
                        File.Move(temporary, cachedPath, true);
                    }
                    finally
                    {
                        if (File.Exists(temporary)) File.Delete(temporary);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // 保存できなくても、この起動中は生成済みの画像を表示する。
                }
                return thumbnail;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
