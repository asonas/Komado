using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MangaViewer;

public partial class MainWindow : Window
{
    private readonly ReaderPosition position = new();
    private readonly string preferencesPath;
    private readonly RecentFiles recentFiles;
    private readonly ThumbnailCache thumbnailCache;
    private readonly CancellationTokenSource thumbnailCancellation = new();
    private BookDocument? book;
    private CancellationTokenSource renderCancellation = new();
    private CancellationTokenSource prefetchCancellation = new();
    private Task renderingTask = Task.CompletedTask;
    private int displayVersion;
    private (int Index, int Count, int Total, bool LeftAdvances)? displayedPage;
    private int openVersion;
    private bool updatingSeek;
    private bool fullscreen;
    private bool automaticLayout;
    private bool closed;
    private WindowState previousState;
    private Rect previousBounds;
    private readonly DispatcherTimer keyRepeatTimer = new();
    private Key heldPageKey = Key.None;
    private int keyTurnsInProgress;

    public MainWindow() : this(WindowPreferences.DefaultPath) { }

    public MainWindow(string preferencesPath)
    {
        this.preferencesPath = preferencesPath;
        recentFiles = new RecentFiles(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(preferencesPath))!, "recent-files.json"));
        thumbnailCache = new ThumbnailCache(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(preferencesPath))!, "Cache", "Thumbnails"));
        InitializeComponent();
        UpdateRecentFiles();
        keyRepeatTimer.Tick += KeyRepeat_Tick;
        var settings = WindowPreferences.Load(preferencesPath);
        Width = Math.Clamp(settings.Width, MinWidth, Math.Max(MinWidth, SystemParameters.WorkArea.Width));
        Height = Math.Clamp(settings.Height, MinHeight, Math.Max(MinHeight, SystemParameters.WorkArea.Height));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        automaticLayout = settings.AutomaticLayout;
        position.Facing = automaticLayout || settings.Facing;
        position.LeftAdvances = settings.LeftAdvances;
        if (settings.Maximized) WindowState = WindowState.Maximized;
        UpdateButtons();
    }

    public ReaderPosition Position => position;
    public double LastDisplayMilliseconds { get; private set; }

    public async Task<bool> OpenBookAsync(string path)
    {
        StopKeyRepeat();
        int version = ++openVersion;
        LoadingText.Visibility = Visibility.Visible;
        ErrorBox.Visibility = Visibility.Collapsed;
        try
        {
            var candidate = await Task.Run(() => BookDocument.Open(path));
            await candidate.GetPageAsync(0);
            if (version != openVersion || closed) return false;
            renderCancellation.Cancel();
            renderCancellation.Dispose();
            renderCancellation = new CancellationTokenSource();
            book = candidate;
            position.Open(candidate.Count);
            SetSeekVisibility(Visibility.Collapsed);
            EmptyState.Visibility = Visibility.Collapsed;
            ReaderButtons.Visibility = Visibility.Visible;
            UpdateButtons();
            await DisplayAsync();
            if (version == openVersion && !closed)
            {
                try
                {
                    recentFiles.Record(path);
                    UpdateRecentFiles();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    ShowError("ファイルは開けましたが、履歴を保存できませんでした。\n" + exception.Message);
                }
            }
            return true;
        }
        catch (Exception exception) when (IsReadError(exception))
        {
            if (version == openVersion && !closed)
                ShowError("開けませんでした。\n" + ReadErrorMessage(exception));
            return false;
        }
        finally
        {
            if (version == openVersion) LoadingText.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateRecentFiles()
    {
        RecentItems.ItemsSource = recentFiles.Entries;
        RecentSection.Visibility = recentFiles.Entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RecentFile_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: string path }) await OpenBookAsync(path);
    }

    private async void RecentThumbnail_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image { DataContext: RecentFile entry } image) return;
        try
        {
            var thumbnail = await thumbnailCache.GetAsync(entry.FullPath, thumbnailCancellation.Token);
            if (!closed && ReferenceEquals(image.DataContext, entry)) image.Source = thumbnail;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (IsReadError(exception))
        {
            image.ToolTip = "表紙を読み込めませんでした。";
        }
    }

    public Task DisplayAsync()
    {
        if (book is null || closed) return Task.CompletedTask;
        displayVersion++;
        prefetchCancellation.Cancel();
        if (renderingTask.IsCompleted) renderingTask = RenderPendingAsync();
        return renderingTask;
    }

    private async Task RenderPendingAsync()
    {
        while (book is not null && !closed)
        {
            int version = displayVersion;
            var token = renderCancellation.Token;
            var current = book;
            int index = position.Index;
            int count = position.VisibleCount;
            bool leftAdvances = position.LeftAdvances;
            var watch = Stopwatch.StartNew();
            try
            {
                BitmapSource first = await current.GetPageAsync(index, token);
                token.ThrowIfCancellationRequested();
                position.SetLandscape(index, first.PixelWidth > first.PixelHeight);
                BitmapSource? second = null;
                if (count > 1 && first.PixelWidth <= first.PixelHeight)
                {
                    var candidate = await current.GetPageAsync(index + 1, token);
                    token.ThrowIfCancellationRequested();
                    bool landscape = candidate.PixelWidth > candidate.PixelHeight;
                    position.SetLandscape(index + 1, landscape);
                    if (!landscape) second = candidate;
                }
                count = second is null ? 1 : 2;
                LeftPage.Source = leftAdvances && second is not null ? second : first;
                RightPage.Source = leftAdvances && second is not null ? first : second;
                RightPage.Visibility = second is null ? Visibility.Collapsed : Visibility.Visible;
                displayedPage = (index, count, current.Count, leftAdvances);
                UpdateSeek();
                LayoutPages();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                LastDisplayMilliseconds = watch.Elapsed.TotalMilliseconds;
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) when (IsReadError(exception))
            {
                if (!token.IsCancellationRequested && version == displayVersion)
                    ShowError("このページを表示できませんでした。\n" + ReadErrorMessage(exception));
            }
            if (closed || version != displayVersion) continue;
            prefetchCancellation.Dispose();
            prefetchCancellation = new CancellationTokenSource();
            _ = PrefetchAsync(current, index, count, prefetchCancellation.Token);
            return;
        }
    }

    private static async Task PrefetchAsync(BookDocument document, int index, int visibleCount, CancellationToken token)
    {
        try
        {
            foreach (int page in new[] { index + visibleCount, index + visibleCount + 1, index - 1, index - 2 })
                if (page >= 0 && page < document.Count)
                    await document.GetPageAsync(page, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (IsReadError(exception))
        {
            // A failed speculative read is reported only if that page is opened.
        }
    }

    private void LayoutPages()
    {
        if (LeftPage.Source is not BitmapSource left) return;
        var right = RightPage.Source as BitmapSource;
        double gap = right is null ? 0 : 8;
        double widths = left.PixelWidth + (right?.PixelWidth ?? 0);
        double heights = Math.Max(left.PixelHeight, right?.PixelHeight ?? 0);
        double scale = Math.Max(0, Math.Min((ReadingArea.ActualWidth - gap) / widths, ReadingArea.ActualHeight / heights));
        LeftPage.Width = left.PixelWidth * scale;
        LeftPage.Height = left.PixelHeight * scale;
        RightPage.Margin = new Thickness(gap, 0, 0, 0);
        if (right is not null)
        {
            RightPage.Width = right.PixelWidth * scale;
            RightPage.Height = right.PixelHeight * scale;
        }
    }

    private void UpdateSeek()
    {
        if (displayedPage is not { } page) return;
        updatingSeek = true;
        SeekSlider.Maximum = Math.Max(0, page.Total - 1);
        SeekSlider.IsDirectionReversed = page.LeftAdvances;
        SeekSlider.Value = page.Index;
        int end = page.Index + page.Count;
        PageNumber.Text = page.Count > 1
            ? $"{page.Index + 1}–{end} / {page.Total}"
            : $"{page.Index + 1} / {page.Total}";
        updatingSeek = false;
    }

    private void UpdateButtons()
    {
        AutoButton.Background = automaticLayout ? (Brush)FindResource("RaisedBrush") : Brushes.Transparent;
        SingleButton.Background = !automaticLayout && !position.Facing ? (Brush)FindResource("RaisedBrush") : Brushes.Transparent;
        FacingButton.Background = !automaticLayout && position.Facing ? (Brush)FindResource("RaisedBrush") : Brushes.Transparent;
        DirectionButton.Content = position.LeftAdvances ? "左で次へ" : "右で次へ";
        LeftDirection.IsChecked = position.LeftAdvances;
        RightDirection.IsChecked = !position.LeftAdvances;
    }

    private async void ReadingArea_Click(object sender, MouseButtonEventArgs e)
    {
        if (book is null || ErrorBox.Visibility == Visibility.Visible) return;
        double fraction = e.GetPosition(ReadingArea).X / ReadingArea.ActualWidth;
        if (fraction is >= 1.0 / 3 and <= 2.0 / 3)
            SetSeekVisibility(SeekBox.Visibility == Visibility.Visible && SeekBox.IsHitTestVisible ? Visibility.Collapsed : Visibility.Visible);
        else
        {
            position.MoveHorizontal(fraction < 1.0 / 3);
            await DisplayAsync();
        }
        e.Handled = true;
    }

    private async void ReadingArea_Wheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && (source == SeekBox || SeekBox.IsAncestorOf(source)))
        {
            e.Handled = true;
            return;
        }
        if (book is null || e.Delta == 0 || ErrorBox.Visibility == Visibility.Visible) return;
        position.Advance(e.Delta < 0);
        e.Handled = true;
        await DisplayAsync();
    }

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        bool pageKey = e.Key is Key.Left or Key.S or Key.J or Key.Right or Key.F or Key.L;
        if (!pageKey) StopKeyRepeat();
        if (e.Key == Key.Escape)
        {
            if (DirectionMenu.IsOpen) DirectionMenu.IsOpen = false;
            else if (ErrorBox.Visibility == Visibility.Visible) ErrorBox.Visibility = Visibility.Collapsed;
            else if (SeekBox.Visibility == Visibility.Visible) SetSeekVisibility(Visibility.Collapsed);
            else if (fullscreen) ToggleFullscreen();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
        {
            var dialog = new OpenFileDialog { Filter = "漫画ファイル|*.zip;*.cbz;*.pdf|PDF|*.pdf|ZIP・CBZ|*.zip;*.cbz", Title = "漫画を開く" };
            if (dialog.ShowDialog(this) == true) await OpenBookAsync(dialog.FileName);
            e.Handled = true;
            return;
        }
        if (book is null || DirectionMenu.IsOpen || ErrorBox.Visibility == Visibility.Visible
            || Keyboard.FocusedElement is TextBox || Keyboard.Modifiers != ModifierKeys.None) return;
        if (!pageKey) return;
        e.Handled = true;
        if (heldPageKey == e.Key) return;
        StopKeyRepeat();
        heldPageKey = e.Key;
        keyRepeatTimer.Interval = TimeSpan.FromMilliseconds((SystemParameters.KeyboardDelay + 1) * 250);
        keyRepeatTimer.Start();
        await TurnHeldKeyAsync();
    }

    private async Task TurnHeldKeyAsync(bool repeat = false)
    {
        if ((repeat && keyTurnsInProgress > 0) || heldPageKey == Key.None) return;
        keyTurnsInProgress++;
        try
        {
            int previousIndex = position.Index;
            position.MoveHorizontal(heldPageKey is Key.Left or Key.S or Key.J);
            if (position.Index != previousIndex)
            {
                FadeOutSeek();
                await DisplayAsync();
            }
        }
        finally
        {
            keyTurnsInProgress--;
        }
    }

    private void SetSeekVisibility(Visibility visibility)
    {
        SeekBox.BeginAnimation(OpacityProperty, null);
        SeekBox.Opacity = 1;
        SeekBox.IsHitTestVisible = true;
        SeekBox.Visibility = visibility;
    }

    private void FadeOutSeek()
    {
        if (SeekBox.Visibility != Visibility.Visible || !SeekBox.IsHitTestVisible) return;
        SeekBox.IsHitTestVisible = false;
        var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
        animation.Completed += (_, _) => SetSeekVisibility(Visibility.Collapsed);
        SeekBox.BeginAnimation(OpacityProperty, animation);
    }

    private async void KeyRepeat_Tick(object? sender, EventArgs e)
    {
        if (book is null || DirectionMenu.IsOpen || ErrorBox.Visibility == Visibility.Visible
            || Keyboard.Modifiers != ModifierKeys.None)
        {
            StopKeyRepeat();
            return;
        }
        keyRepeatTimer.Interval = TimeSpan.FromMilliseconds(1000 / (2.5 + 27.5 * SystemParameters.KeyboardSpeed / 31));
        await TurnHeldKeyAsync(repeat: true);
    }

    private void Window_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != heldPageKey) return;
        StopKeyRepeat();
        e.Handled = true;
    }

    private void Window_Deactivated(object? sender, EventArgs e) => StopKeyRepeat();

    private void StopKeyRepeat()
    {
        keyRepeatTimer.Stop();
        heldPageKey = Key.None;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        bool valid = e.Data.GetDataPresent(DataFormats.FileDrop)
            && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files
            && (Directory.Exists(files[0]) || Path.GetExtension(files[0]).ToLowerInvariant() is ".zip" or ".cbz" or ".pdf");
        e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        DropHint.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths)
            await OpenBookAsync(paths[0]);
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        var point = e.GetPosition(this);
        if (point.X <= 0 || point.Y <= 0 || point.X >= ActualWidth || point.Y >= ActualHeight)
            DropHint.Visibility = Visibility.Collapsed;
    }

    private async void Seek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updatingSeek || book is null) return;
        position.Seek((int)Math.Round(e.NewValue));
        UpdateSeek();
        await DisplayAsync();
    }

    private async void Single_Click(object sender, RoutedEventArgs e)
    {
        automaticLayout = false;
        position.Facing = false;
        UpdateButtons();
        await DisplayAsync();
    }

    private async void Facing_Click(object sender, RoutedEventArgs e)
    {
        automaticLayout = false;
        position.Facing = true;
        UpdateButtons();
        await DisplayAsync();
    }

    private async void Auto_Click(object sender, RoutedEventArgs e)
    {
        automaticLayout = true;
        position.Facing = true;
        UpdateButtons();
        await DisplayAsync();
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => LayoutPages();
    private void Direction_Click(object sender, RoutedEventArgs e)
    {
        DirectionMenu.PlacementTarget = DirectionButton;
        DirectionMenu.IsOpen = true;
    }

    private async void LeftDirection_Click(object sender, RoutedEventArgs e)
    {
        position.LeftAdvances = true;
        UpdateButtons();
        await DisplayAsync();
    }

    private async void RightDirection_Click(object sender, RoutedEventArgs e)
    {
        position.LeftAdvances = false;
        UpdateButtons();
        await DisplayAsync();
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void ToggleFullscreen()
    {
        if (!fullscreen)
        {
            previousState = WindowState;
            previousBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            fullscreen = true;
            WindowState = WindowState.Normal;
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0) });
            ResizeMode = ResizeMode.NoResize;
            TitleRow.Height = new GridLength(0);
            TitleBar.Visibility = Visibility.Collapsed;
            WindowState = WindowState.Maximized;
        }
        else
        {
            fullscreen = false;
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.CanResize;
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 36, ResizeBorderThickness = new Thickness(5), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
            Left = previousBounds.Left;
            Top = previousBounds.Top;
            Width = previousBounds.Width;
            Height = previousBounds.Height;
            TitleRow.Height = new GridLength(36);
            TitleBar.Visibility = Visibility.Visible;
            WindowState = previousState;
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void DismissError_Click(object sender, RoutedEventArgs e) => ErrorBox.Visibility = Visibility.Collapsed;
    private void Overlay_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;
    private void Overlay_Wheel(object sender, MouseWheelEventArgs e) => e.Handled = true;
    private void ReadingArea_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutPages();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (Root is not null) Root.Margin = WindowState == WindowState.Maximized && !fullscreen ? new Thickness(7) : new Thickness(0);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        StopKeyRepeat();
        var bounds = fullscreen ? previousBounds : WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        bool maximized = fullscreen ? previousState == WindowState.Maximized : WindowState == WindowState.Maximized;
        try
        {
            new WindowPreferences(bounds.Width, bounds.Height, maximized, position.Facing, position.LeftAdvances, automaticLayout).Save(preferencesPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowError("ウィンドウサイズを保存できませんでした。\n" + exception.Message);
            e.Cancel = true;
            return;
        }
        closed = true;
        thumbnailCancellation.Cancel();
        openVersion++;
        renderCancellation.Cancel();
        renderCancellation.Dispose();
        prefetchCancellation.Cancel();
        prefetchCancellation.Dispose();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBox.Visibility = Visibility.Visible;
    }

    private static bool IsReadError(Exception exception) => exception is IOException or UnauthorizedAccessException
        or InvalidDataException or NotSupportedException or ArgumentException or COMException or FileFormatException;

    private static string ReadErrorMessage(Exception exception) => exception is InvalidDataException
        ? exception.Message : "ファイルが壊れているか、アクセスできない形式です。";
}
