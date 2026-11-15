using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using StorageFile = Windows.Storage.StorageFile;
using Windows.Storage.Streams;

namespace MangaViewer;

public sealed class BookDocument
{
    private const long MaximumImageBytes = 128L * 1024 * 1024;
    private const long CacheBudget = 192L * 1024 * 1024;
    private readonly string path;
    private readonly PageEntry[] pages;
    private readonly bool archive;
    private readonly PdfDocument? pdf;
    private readonly SemaphoreSlim decoderGate = new(1);
    private readonly Dictionary<int, (BitmapSource Image, long Size, long Used)> cache = [];
    private long cacheBytes;
    private long clock;

    private BookDocument(string path, PageEntry[] pages, bool archive, PdfDocument? pdf = null)
    {
        this.path = path;
        this.pages = pages;
        this.archive = archive;
        this.pdf = pdf;
    }

    public int Count => pages.Length;
    public string PageName(int index) => pages[index].Name;

    public static BookDocument Open(string path)
    {
        if (!Directory.Exists(path) && Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return Task.Run(() => OpenPdfAsync(path)).GetAwaiter().GetResult();
        PageEntry[] entries;
        bool archive = !Directory.Exists(path);
        if (archive)
        {
            if (!new[] { ".zip", ".cbz" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("ZIP・PDF ファイルまたは画像フォルダを選んでください。");
            using var zip = ZipFile.OpenRead(path);
            entries = zip.Entries.Select((entry, index) => new PageEntry(entry.FullName, index))
                .Where(entry => IsImage(entry.Name) && !entry.Name.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        else
        {
            entries = Directory.EnumerateFiles(path, "*", new EnumerationOptions
                { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint })
                .Where(IsImage).Select(file => new PageEntry(Path.GetRelativePath(path, file), -1)).ToArray();
        }
        Array.Sort(entries, (a, b) => NaturalCompare(a.Name, b.Name));
        if (entries.Length == 0)
            throw new InvalidDataException("表示できる画像がありません。PNG・JPEG・BMP・GIF・TIFF に対応しています。");
        return new BookDocument(path, entries, archive);
    }

    private static async Task<BookDocument> OpenPdfAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            var document = await PdfDocument.LoadFromFileAsync(file);
            if (document.PageCount == 0)
                throw new InvalidDataException("PDF に表示できるページがありません。");
            var entries = Enumerable.Range(0, checked((int)document.PageCount))
                .Select(index => new PageEntry($"{index + 1}", -1)).ToArray();
            return new BookDocument(path, entries, false, document);
        }
        catch (Exception exception) when (exception.HResult == unchecked((int)0x8007052B))
        {
            throw new InvalidDataException("パスワードで保護された PDF には対応していません。", exception);
        }
        catch (COMException exception)
        {
            throw new InvalidDataException("PDF を読み込めません。ファイルが破損していないか確認してください。", exception);
        }
    }

    public async Task<BitmapSource> GetPageAsync(int index, CancellationToken cancellationToken = default)
    {
        lock (cache)
        {
            if (cache.TryGetValue(index, out var hit))
            {
                cache[index] = (hit.Image, hit.Size, ++clock);
                return hit.Image;
            }
        }
        await decoderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (cache)
            {
                if (cache.TryGetValue(index, out var hit))
                    return hit.Image;
            }
            var image = await Task.Run(() => pdf is null
                ? Task.FromResult(ReadPage(index, cancellationToken))
                : ReadPdfPageAsync(index, cancellationToken), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            long size = (long)image.PixelWidth * image.PixelHeight * 4;
            lock (cache)
            {
                while (cache.Count > 0 && (cacheBytes + size > CacheBudget || cache.Count >= 8))
                {
                    int oldest = cache.MinBy(pair => pair.Value.Used).Key;
                    cacheBytes -= cache[oldest].Size;
                    cache.Remove(oldest);
                }
                cache[index] = (image, size, ++clock);
                cacheBytes += size;
            }
            return image;
        }
        finally
        {
            decoderGate.Release();
        }
    }

    private async Task<BitmapSource> ReadPdfPageAsync(int index, CancellationToken cancellationToken)
    {
        try
        {
            using var page = pdf!.GetPage((uint)index);
            double scale = Math.Min(2160 / page.Size.Height, 4096 / page.Size.Width);
            int width = (int)Math.Max(1, Math.Round(page.Size.Width * scale));
            int height = (int)Math.Max(1, Math.Round(page.Size.Height * scale));
            using var rendered = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(rendered, new PdfPageRenderOptions
            {
                DestinationWidth = (uint)width,
                DestinationHeight = (uint)height,
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
            }).AsTask(cancellationToken).ConfigureAwait(false);
            using var stream = rendered.AsStreamForRead();
            stream.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            // PDF render dimensions are DIPs, so display scaling can enlarge the encoded bitmap.
            bitmap.DecodePixelWidth = width;
            bitmap.DecodePixelHeight = height;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (COMException exception)
        {
            throw new InvalidDataException("PDF のページを描画できませんでした。", exception);
        }
    }

    private BitmapSource ReadPage(int index, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        if (archive)
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.Entries[pages[index].ArchiveIndex];
            if (entry.Length > MaximumImageBytes)
                throw new InvalidDataException("画像ファイルが大きすぎます (上限 128 MB)。");
            using var source = entry.Open();
            CopyImage(source, buffer, cancellationToken);
        }
        else
        {
            using var source = File.OpenRead(Path.Combine(path, pages[index].Name));
            if (source.Length > MaximumImageBytes)
                throw new InvalidDataException("画像ファイルが大きすぎます (上限 128 MB)。");
            CopyImage(source, buffer, cancellationToken);
        }
        buffer.Position = 0;
        var decoder = BitmapDecoder.Create(buffer, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        int height = decoder.Frames[0].PixelHeight;
        int width = decoder.Frames[0].PixelWidth;
        if (height <= 0 || width <= 0)
            throw new InvalidDataException("画像サイズを読み取れません。");
        buffer.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelHeight = Math.Max(1, (int)Math.Min(height, Math.Min(2160, 4096.0 * height / width)));
        bitmap.StreamSource = buffer;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static void CopyImage(Stream source, Stream target, CancellationToken cancellationToken)
    {
        byte[] bytes = new byte[81920];
        int read;
        while ((read = source.Read(bytes)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target.Length + read > MaximumImageBytes)
                throw new InvalidDataException("画像ファイルが大きすぎます (上限 128 MB)。");
            target.Write(bytes, 0, read);
        }
    }

    private static bool IsImage(string file) => Path.GetExtension(file).ToLowerInvariant()
        is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tif" or ".tiff";

    private static int NaturalCompare(string a, string b)
    {
        int comparison = StrCmpLogicalW(a, b);
        return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(a, b);
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string first, string second);

    private sealed record PageEntry(string Name, int ArchiveIndex);
}
