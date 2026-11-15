using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MangaViewer;

internal static class Program
{
    private static readonly string Output = Path.GetFullPath("artifacts/checks");
    private static readonly List<string> Passed = [];

    [STAThread]
    private static int Main()
    {
        Directory.CreateDirectory(Output);
        var app = new App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        int result = 0;
        var frame = new DispatcherFrame();
        _ = RunAsync().ContinueWith(task => app.Dispatcher.BeginInvoke(() =>
        {
            if (task.IsFaulted)
            {
                Console.Error.WriteLine(task.Exception!.GetBaseException());
                result = 1;
            }
            frame.Continue = false;
        }));
        Dispatcher.PushFrame(frame);
        app.Shutdown();
        return result;
    }

    private static async Task RunAsync()
    {
        string folder = Path.Combine(Output, "pages");
        Directory.CreateDirectory(folder);
        foreach (int number in new[] { 10, 1, 3, 2, 4, 5, 6, 7, 8, 9, 11, 12 })
            CreatePage(Path.Combine(folder, $"page{number}.png"), number);
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "Not an image");
        string zipPath = Path.Combine(Output, "sample.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(folder, zipPath);

        var zipped = BookDocument.Open(zipPath);
        var directory = BookDocument.Open(folder);
        Equal(12, zipped.Count, "ZIP image filtering");
        Equal("page2.png", zipped.PageName(1), "ZIP natural ordering");
        Equal("page10.png", directory.PageName(9), "folder natural ordering");
        var zipPage = await zipped.GetPageAsync(1);
        var folderPage = await directory.GetPageAsync(1);
        Equal(Hash(zipPage), Hash(folderPage), "ZIP and folder produce identical decoded pixels");
        True(ReferenceEquals(zipPage, await zipped.GetPageAsync(1)), "decoded page cache reuse");

        string thumbnailDirectory = Path.Combine(Output, "thumbnail-tests", Guid.NewGuid().ToString("N"));
        var thumbnails = new ThumbnailCache(thumbnailDirectory);
        var zipThumbnail = await thumbnails.GetAsync(zipPath);
        Equal(256, zipThumbnail.PixelWidth, "thumbnail is a 256 pixel square");
        Equal(256, zipThumbnail.PixelHeight, "thumbnail square height");
        True(zipThumbnail.IsFrozen, "background thumbnail can be displayed on UI thread");
        string cachedThumbnail = Directory.GetFiles(thumbnailDirectory, "*.png").Single();
        var cacheWriteTime = File.GetLastWriteTimeUtc(cachedThumbnail);
        Equal(Hash(zipThumbnail), Hash(await new ThumbnailCache(thumbnailDirectory).GetAsync(zipPath)), "thumbnail survives a new cache instance");
        Equal(cacheWriteTime, File.GetLastWriteTimeUtc(cachedThumbnail), "persistent cache hit does not regenerate the thumbnail");
        Equal(Hash(zipThumbnail), Hash(await thumbnails.GetAsync(folder)), "folder and ZIP use the same cover");
        File.WriteAllText(cachedThumbnail, "broken cache");
        Equal(Hash(zipThumbnail), Hash(await thumbnails.GetAsync(zipPath)), "damaged thumbnail cache is regenerated");
        File.SetLastWriteTimeUtc(zipPath, File.GetLastWriteTimeUtc(zipPath).AddSeconds(5));
        await thumbnails.GetAsync(zipPath);
        Equal(3, Directory.GetFiles(thumbnailDirectory, "*.png").Length, "updated ZIP creates a new cached thumbnail");

        foreach (bool landscape in new[] { false, true })
        {
            string cropFolder = Path.Combine(Output, landscape ? "crop-landscape" : "crop-portrait");
            Directory.CreateDirectory(cropFolder);
            int width = landscape ? 400 : 200;
            int height = landscape ? 200 : 400;
            byte[] pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int axis = landscape ? x : y;
                int offset = (y * width + x) * 4;
                pixels[offset] = (byte)(axis >= 100 && axis < 300 ? 255 : 0);
                pixels[offset + 1] = (byte)(axis >= 300 ? 255 : 0);
                pixels[offset + 2] = (byte)(axis < 100 ? 255 : 0);
                pixels[offset + 3] = 255;
            }
            Save(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4), Path.Combine(cropFolder, "1.png"));
            CreatePage(Path.Combine(cropFolder, "2.png"), 2);
            var cropped = await thumbnails.GetAsync(cropFolder);
            foreach (var point in new[] { new Point(8, 128), new Point(248, 128), new Point(128, 8), new Point(128, 248) })
            {
                byte[] pixel = new byte[4];
                cropped.CopyPixels(new Int32Rect((int)point.X, (int)point.Y, 1, 1), pixel, 4, 0);
                True(pixel.SequenceEqual(new byte[] { 255, 0, 0, 255 }), $"{(landscape ? "landscape" : "portrait")} first page is cropped from its center at {point}");
            }
        }

        var reader = new ReaderPosition();
        reader.Open(6);
        Equal(1, reader.VisibleCount, "cover is displayed alone");
        reader.MoveHorizontal(true);
        Equal(1, reader.Index, "cover advances to pages 2 and 3");
        reader.Advance(false);
        Equal(0, reader.Index, "first spread returns to cover");
        reader.Advance(true);
        reader.Advance(true);
        Equal(3, reader.Index, "subsequent spreads advance by two");
        reader.Advance(true);
        Equal(5, reader.Index, "unpaired last page");
        Equal(1, reader.VisibleCount, "unpaired last page displayed alone");
        reader.Advance(true);
        Equal(5, reader.Index, "end boundary");
        reader.LeftAdvances = false;
        reader.MoveHorizontal(true);
        Equal(3, reader.Index, "direction reversal");
        reader.Facing = false;
        reader.Advance(false);
        Equal(2, reader.Index, "single page step");
        reader.Seek(-100);
        reader.Advance(false);
        Equal(0, reader.Index, "start boundary");

        string settings = Path.Combine(Output, "window.json");
        string historyPath = Path.Combine(Output, "recent-files.json");
        File.WriteAllText(historyPath, "[]");
        File.WriteAllText(settings, "invalid JSON");
        Equal(1200d, WindowPreferences.Load(settings).Width, "corrupt preferences recovery");
        new WindowPreferences(1100, 760).Save(settings);
        var window = new MainWindow(settings);
        window.Show();
        await WaitForLayout();
        Capture(window, "empty.png");
        Equal(Visibility.Collapsed, Control<StackPanel>(window, "RecentSection").Visibility, "recent list hidden before first successful open");
        True(await window.OpenBookAsync(zipPath), "ZIP opens in real window");
        await WaitForLayout();
        var readingArea = Control<Grid>(window, "ReadingArea");
        var left = Control<Image>(window, "LeftPage");
        var right = Control<Image>(window, "RightPage");
        Equal(Hash(await zipped.GetPageAsync(0)), Hash((BitmapSource)left.Source), "opening book displays cover");
        Equal(Visibility.Collapsed, right.Visibility, "cover has no second image");
        True(Math.Abs(left.TranslatePoint(new Point(left.ActualWidth / 2, 0), readingArea).X
            - readingArea.ActualWidth / 2) < 1, "cover is horizontally centered");
        True(Math.Abs(left.ActualHeight - readingArea.ActualHeight) < 1, "pages fill available height");
        var seek = Control<Border>(window, "SeekBox");
        Equal(Visibility.Collapsed, seek.Visibility, "seek starts hidden");
        Capture(window, "cover.png");
        var slider = Control<Slider>(window, "SeekSlider");
        var pageNumber = Control<TextBlock>(window, "PageNumber");
        var coverImage = left.Source;
        slider.Value = 9;
        Equal(0d, slider.Value, "seek stays on displayed page while uncached image loads");
        Equal("1 / 12", pageNumber.Text, "page number stays with displayed image while loading");
        True(ReferenceEquals(coverImage, left.Source), "previous image remains visible while loading");
        await window.DisplayAsync();
        Equal(9d, slider.Value, "seek commits with decoded image");
        Equal("10–11 / 12", pageNumber.Text, "page number commits with decoded spread");
        Equal(Hash(await zipped.GetPageAsync(10)), Hash((BitmapSource)left.Source), "seek commits matching left page pixels");
        slider.Value = 3;
        slider.Value = 8;
        slider.Value = 5;
        await window.DisplayAsync();
        Equal(5d, slider.Value, "rapid seeks finish at latest requested page");
        Equal(Hash(await zipped.GetPageAsync(6)), Hash((BitmapSource)left.Source), "rapid seeks display latest requested pixels");
        window.Position.Seek(0);
        await window.DisplayAsync();
        window.Position.Advance(true);
        await window.DisplayAsync();
        Equal(Hash(await zipped.GetPageAsync(2)), Hash((BitmapSource)left.Source), "RTL left image is page 3 after cover");
        Equal(Hash(await zipped.GetPageAsync(1)), Hash((BitmapSource)right.Source), "RTL right image is page 2 after cover");
        Capture(window, "reading.png");

        seek.Visibility = Visibility.Visible;
        SendKey(window, Key.Right);
        Equal(Visibility.Visible, seek.Visibility, "seek remains visible while fading");
        True(!seek.IsHitTestVisible, "fading seek stops intercepting input");
        await WaitUntil(() => seek.Visibility == Visibility.Collapsed);
        Equal(1d, seek.Opacity, "seek opacity resets for reopening");
        await WaitUntil(() => window.Position.Index == 0);
        HoldKey(window, Key.Left);
        await Task.Delay((SystemParameters.KeyboardDelay + 1) * 250 + 240);
        True(window.Position.Index >= 3, "held left arrow continuously advances beyond first spread");
        int displayedIndex = (int)slider.Value;
        var heldImage = left.Source;
        int displayedLeftIndex = displayedIndex == 0 ? 0 : Math.Min(displayedIndex + 1, zipped.Count - 1);
        Equal(Hash(await zipped.GetPageAsync(displayedLeftIndex)), Hash((BitmapSource)heldImage),
            "held arrow displays pixels matching seek position before release");
        ReleaseKey(window, Key.Left);
        int releasedIndex = window.Position.Index;
        await Task.Delay(200);
        Equal(releasedIndex, window.Position.Index, "key release stops continuous navigation");
        HoldKey(window, Key.Right);
        await Task.Delay((SystemParameters.KeyboardDelay + 1) * 250 + 240);
        True(window.Position.Index < releasedIndex, "held right arrow continuously goes back");
        ReleaseKey(window, Key.Right);

        foreach (var key in new[] { Key.Left, Key.S, Key.J })
        {
            window.Position.Seek(1);
            SendKey(window, key);
            await WaitUntil(() => window.Position.Index == 3);
        }
        Equal(3, window.Position.Index, "left / S / J inputs");
        foreach (var key in new[] { Key.Right, Key.F, Key.L })
        {
            window.Position.Seek(3);
            SendKey(window, key);
            await WaitUntil(() => window.Position.Index == 1);
        }
        Equal(1, window.Position.Index, "right / F / L inputs");
        readingArea.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
            { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        await WaitUntil(() => window.Position.Index == 3);
        Equal(3, window.Position.Index, "wheel down advances");

        Click(window, "DirectionButton");
        await WaitForLayout();
        Capture(window, "direction.png");
        Control<MenuItem>(window, "RightDirection").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Control<ContextMenu>(window, "DirectionMenu").IsOpen = false;
        await WaitForLayout();
        Equal(false, window.Position.LeftAdvances, "direction button changes input setting");
        Equal(3, window.Position.Index, "direction change preserves position");
        SendKey(window, Key.Right);
        await WaitUntil(() => window.Position.Index == 5);
        Equal(5, window.Position.Index, "right advances after setting change");
        Click(window, "SingleButton");
        await WaitForLayout();
        Equal(Visibility.Collapsed, right.Visibility, "single page hides second image");
        Equal(5, window.Position.Index, "layout change preserves position");
        Click(window, "FacingButton");
        await WaitForLayout();

        double heightBefore = left.ActualHeight;
        seek.Visibility = Visibility.Visible;
        await WaitForLayout();
        True(Math.Abs(seek.ActualWidth - 640) < 1, "seek box max width");
        Equal(heightBefore, left.ActualHeight, "seek overlay does not resize page");
        Control<Slider>(window, "SeekSlider").Value = 7;
        await WaitUntil(() => window.Position.Index == 7);
        Equal(7, window.Position.Index, "seek jumps to page");
        Capture(window, "seek.png");
        seek.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
            { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        Equal(7, window.Position.Index, "overlay wheel does not turn page");

        var times = new List<double>();
        for (int i = 0; i < 8; i++)
        {
            window.Position.Seek(i);
            await window.DisplayAsync();
            times.Add(window.LastDisplayMilliseconds);
        }
        File.WriteAllText(Path.Combine(Output, "timings.txt"), string.Join(Environment.NewLine,
            times.Select((time, index) => $"display {index + 1}: {time:F2} ms")));

        string invalid = Path.Combine(Output, "bad.zip");
        File.WriteAllText(invalid, "broken");
        Equal(false, await window.OpenBookAsync(invalid), "invalid ZIP rejected");
        Equal(zipPath, new RecentFiles(historyPath).Entries[0].FullPath, "failed open does not enter recent history");
        Equal(7, window.Position.Index, "invalid book preserves current book");
        Click(window, "FullscreenButton");
        await WaitForLayout();
        Equal(Visibility.Collapsed, Control<Grid>(window, "TitleBar").Visibility, "fullscreen hides titlebar");
        SendKey(window, Key.Escape);
        SendKey(window, Key.Escape);
        SendKey(window, Key.Escape);
        await WaitForLayout();
        Equal(Visibility.Visible, Control<Grid>(window, "TitleBar").Visibility, "Escape restores titlebar");
        window.Width = 1040;
        window.Height = 720;
        window.Close();
        var reopened = new MainWindow(settings);
        Equal(1040d, reopened.Width, "window width restored");
        Equal(720d, reopened.Height, "window height restored");
        reopened.Show();
        await WaitForLayout();
        var recentItems = Control<ItemsControl>(reopened, "RecentItems");
        Equal(zipPath, ((RecentFile)recentItems.Items[0]).FullPath, "recent ZIP is restored on start screen after restart");
        var recentPresenter = (ContentPresenter)recentItems.ItemContainerGenerator.ContainerFromIndex(0);
        var recentButton = (Button)recentPresenter.ContentTemplate.FindName("RecentFileButton", recentPresenter);
        var recentImage = (Image)recentPresenter.ContentTemplate.FindName("RecentThumbnail", recentPresenter);
        await WaitUntil(() => recentImage.Source is BitmapSource);
        Equal(176d, recentImage.ActualWidth, "recent cover has a square display width");
        Equal(176d, recentImage.ActualHeight, "recent cover has a square display height");
        Equal(Hash(zipThumbnail), Hash((BitmapSource)recentImage.Source), "recent card displays the generated cover");
        Capture(reopened, "recent-files.png");
        recentButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await WaitUntil(() => Control<ScrollViewer>(reopened, "EmptyState").Visibility == Visibility.Collapsed);
        Equal(12, reopened.Position.Count, "recent file click reopens the book");
        True(await reopened.OpenBookAsync(folder), "folder opens after restart");
        Equal(folder, new RecentFiles(historyPath).Entries[0].FullPath, "recent folder precedes older ZIP");
        True(await reopened.OpenBookAsync(zipPath), "previous ZIP can be reopened");
        var recentHistory = new RecentFiles(historyPath);
        Equal(2, recentHistory.Entries.Count, "reopened files are deduplicated");
        Equal(zipPath, recentHistory.Entries[0].FullPath, "reopened file moves to top of history");
        string mixedFolder = Path.Combine(Output, "mixed-pages");
        Directory.CreateDirectory(mixedFolder);
        for (int number = 1; number <= 7; number++)
            CreatePage(Path.Combine(mixedFolder, $"page{number}.png"), number, landscape: number is 3 or 6);
        string mixedZip = Path.Combine(Output, "mixed.zip");
        if (File.Exists(mixedZip)) File.Delete(mixedZip);
        ZipFile.CreateFromDirectory(mixedFolder, mixedZip);
        string pdfPath = Path.Combine(Output, "sample.PDF");
        CreatePdf(pdfPath);
        var pdf = BookDocument.Open(pdfPath);
        Equal(7, pdf.Count, "PDF page count follows document order");
        var pdfTimes = new List<string>();
        for (int i = 0; i < pdf.Count; i++)
        {
            var watch = Stopwatch.StartNew();
            var page = await pdf.GetPageAsync(i);
            pdfTimes.Add($"page {i + 1}: {watch.Elapsed.TotalMilliseconds:F2} ms");
            True(page.IsFrozen, "PDF page can cross threads");
            Equal(i is 2 or 5, page.PixelWidth > page.PixelHeight, "PDF preserves page orientation");
            True(page.PixelWidth <= 4096 && page.PixelHeight <= 2160, $"PDF rendering stays within image budget ({page.PixelWidth} x {page.PixelHeight})");
            var converted = new FormatConvertedBitmap(page, PixelFormats.Bgra32, null, 0);
            byte[] pixel = new byte[4];
            converted.CopyPixels(new Int32Rect(page.PixelWidth / 2, page.PixelHeight / 2, 1, 1), pixel, 4, 0);
            byte[][] colors = [[0, 0, 255, 255], [0, 255, 0, 255], [255, 0, 0, 255]];
            True(pixel.SequenceEqual(colors[i % 3]), "PDF renders the expected page content");
            watch.Restart();
            True(ReferenceEquals(page, await pdf.GetPageAsync(i)), "PDF reuses decoded page cache");
            pdfTimes.Add($"cached page {i + 1}: {watch.Elapsed.TotalMilliseconds:F2} ms");
        }
        File.WriteAllLines(Path.Combine(Output, "pdf-timings.txt"), pdfTimes);
        string pdfThumbnailDirectory = Path.Combine(Output, "pdf-thumbnails", Guid.NewGuid().ToString("N"));
        var pdfThumbnails = new ThumbnailCache(pdfThumbnailDirectory);
        var pdfCover = await pdfThumbnails.GetAsync(pdfPath);
        Equal(256, pdfCover.PixelWidth, "PDF cover thumbnail is generated");
        Equal(Hash(pdfCover), Hash(await new ThumbnailCache(pdfThumbnailDirectory).GetAsync(pdfPath)), "PDF thumbnail persists");
        File.SetLastWriteTimeUtc(pdfPath, File.GetLastWriteTimeUtc(pdfPath).AddSeconds(5));
        await pdfThumbnails.GetAsync(pdfPath);
        Equal(2, Directory.GetFiles(pdfThumbnailDirectory, "*.png").Length, "updated PDF invalidates its thumbnail");
        Click(reopened, "AutoButton");
        foreach (string source in new[] { mixedFolder, mixedZip, pdfPath })
        {
            True(await reopened.OpenBookAsync(source), "mixed orientation book opens");
            int[] starts = [0, 1, 2, 3, 5, 6];
            int[] counts = [1, 1, 1, 2, 1, 1];
            for (int i = 0; i < starts.Length; i++)
            {
                Equal(starts[i], reopened.Position.Index, "mixed orientation navigation preserves every page");
                Equal(counts[i], reopened.Position.VisibleCount, "landscape and adjacent portrait are shown alone");
                var mixedLeft = Control<Image>(reopened, "LeftPage");
                var mixedRight = Control<Image>(reopened, "RightPage");
                Equal(counts[i] == 2 ? Visibility.Visible : Visibility.Collapsed, mixedRight.Visibility,
                    "mixed orientation image count matches navigation");
                if (starts[i] == 2)
                {
                    True(((BitmapSource)mixedLeft.Source).PixelWidth > ((BitmapSource)mixedLeft.Source).PixelHeight,
                        "landscape dimensions are detected from actual image");
                    var area = Control<Grid>(reopened, "ReadingArea");
                    True(Math.Abs(mixedLeft.TranslatePoint(new Point(mixedLeft.ActualWidth / 2, 0), area).X
                        - area.ActualWidth / 2) < 1, "landscape page is centered");
                    Capture(reopened, "landscape.png");
                }
                if (i < starts.Length - 1)
                {
                    reopened.Position.Advance(true);
                    await reopened.DisplayAsync();
                }
            }
            for (int i = starts.Length - 2; i >= 0; i--)
            {
                reopened.Position.Advance(false);
                await reopened.DisplayAsync();
                Equal(starts[i], reopened.Position.Index, "backward navigation restores mixed orientation spread");
            }
        }
        reopened.Position.Seek(3);
        Click(reopened, "SingleButton");
        await reopened.DisplayAsync();
        Equal(Visibility.Collapsed, Control<Image>(reopened, "RightPage").Visibility, "manual single overrides portrait pairing");
        Click(reopened, "AutoButton");
        await reopened.DisplayAsync();
        Equal(Visibility.Visible, Control<Image>(reopened, "RightPage").Visibility, "automatic mode restores portrait pairing");
        Capture(reopened, "pdf-reading.png");
        string brokenPdf = Path.Combine(Output, "broken.pdf");
        File.WriteAllText(brokenPdf, "Not a PDF");
        var displayedPdf = Control<Image>(reopened, "LeftPage").Source;
        Equal(false, await reopened.OpenBookAsync(brokenPdf), "invalid PDF is rejected");
        True(ReferenceEquals(displayedPdf, Control<Image>(reopened, "LeftPage").Source), "invalid PDF preserves displayed book");
        Equal(pdfPath, new RecentFiles(historyPath).Entries[0].FullPath, "failed PDF does not enter history");
        reopened.Close();
        var gallery = new MainWindow(settings) { Height = 960 };
        gallery.Show();
        await WaitForLayout();
        var galleryItems = Control<ItemsControl>(gallery, "RecentItems");
        var galleryImages = Enumerable.Range(0, galleryItems.Items.Count).Select(index =>
        {
            var presenter = (ContentPresenter)galleryItems.ItemContainerGenerator.ContainerFromIndex(index);
            return (Image)presenter.ContentTemplate.FindName("RecentThumbnail", presenter);
        }).ToArray();
        await WaitUntil(() => galleryImages.All(image => image.Source is BitmapSource));
        True(galleryImages.Length >= 4, "history displays multiple thumbnail cards");
        var firstCardOrigin = galleryImages[0].TranslatePoint(new Point(), Control<Grid>(gallery, "ReadingArea"));
        True(firstCardOrigin.X <= 48 && firstCardOrigin.Y <= 80, "thumbnail grid starts at the top left of the reading area");
        Equal(galleryImages[0].TranslatePoint(new Point(), galleryItems).Y, galleryImages[3].TranslatePoint(new Point(), galleryItems).Y,
            "thumbnail grid uses available window width");
        gallery.Width = 800;
        await WaitForLayout();
        True(galleryImages[3].TranslatePoint(new Point(), galleryItems).Y > galleryImages[0].TranslatePoint(new Point(), galleryItems).Y,
            "thumbnail cards wrap onto the next row");
        gallery.Width = 1040;
        await WaitForLayout();
        Capture(gallery, "recent-files-grid.png");
        Equal(pdfPath, ((RecentFile)galleryItems.Items[0]).FullPath, "PDF history survives restart");
        var pdfPresenter = (ContentPresenter)galleryItems.ItemContainerGenerator.ContainerFromIndex(0);
        var pdfButton = (Button)pdfPresenter.ContentTemplate.FindName("RecentFileButton", pdfPresenter);
        pdfButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await WaitUntil(() => gallery.Position.Count == 7);
        await gallery.DisplayAsync();
        Equal(Hash(await pdf.GetPageAsync(0)), Hash((BitmapSource)Control<Image>(gallery, "LeftPage").Source), "recent PDF click reopens cover");
        gallery.Close();
        File.WriteAllLines(Path.Combine(Output, "results.txt"), Passed);
        foreach (string item in Passed) Console.WriteLine(item);
        Console.WriteLine($"PASS: {Passed.Count} checks. Screenshots and timings: {Output}");
    }

    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static void Click(MainWindow window, string name) => Control<Button>(window, name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void SendKey(MainWindow window, Key key)
    {
        HoldKey(window, key);
        ReleaseKey(window, key);
    }
    private static void HoldKey(MainWindow window, Key key) => window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(window), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static void ReleaseKey(MainWindow window, Key key) => window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(window), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyUpEvent });
    private static async Task WaitForLayout()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(70);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        True(condition(), "event completed");
        await WaitForLayout();
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
        Passed.Add("PASS " + name);
    }

    private static void True(bool actual, string name) => Equal(true, actual, name);

    private static string Hash(BitmapSource image)
    {
        int stride = (image.PixelWidth * image.Format.BitsPerPixel + 7) / 8;
        byte[] bytes = new byte[stride * image.PixelHeight];
        image.CopyPixels(bytes, stride, 0);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    private static void Capture(Window window, string name)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        Save(image, Path.Combine(Output, name));
    }

    private static void CreatePage(string path, int number, bool landscape = false)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb((byte)(228 - number * 3), (byte)(232 - number * 2), 217)), null,
                new Rect(0, 0, landscape ? 900 : 600, landscape ? 600 : 900));
            drawing.DrawRectangle(null, new Pen(Brushes.DarkSlateGray, 4), new Rect(24, 24, 552, 520));
            drawing.DrawRectangle(Brushes.DarkSlateGray, null, new Rect(24, 568, 260, 308));
            drawing.DrawRectangle(null, new Pen(Brushes.DarkSlateGray, 4), new Rect(308, 568, 268, 308));
            drawing.DrawText(new FormattedText(number.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 100, Brushes.DarkSlateGray, 1), new Point(70, 200));
        }
        var bitmap = new RenderTargetBitmap(landscape ? 900 : 600, landscape ? 600 : 900, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        Save(bitmap, path);
    }

    private static void CreatePdf(string path)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Count 7 /Kids [3 0 R 5 0 R 7 0 R 9 0 R 11 0 R 13 0 R 15 0 R] >>"
        };
        for (int i = 0; i < 7; i++)
        {
            int width = i is 2 or 5 ? 900 : 600;
            int height = i is 2 or 5 ? 600 : 900;
            string color = new[] { "1 0 0", "0 1 0", "0 0 1" }[i % 3];
            string content = $"{color} rg 0 0 {width} {height} re f\n";
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << >> /Contents {4 + i * 2} 0 R >>");
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}endstream");
        }
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        int xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets) pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, pdf.ToString(), Encoding.ASCII);
    }

    private static void Save(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
