using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shell;
using Convert.Core;
using Microsoft.Win32;

namespace Convert;

public partial class MainWindow : Window
{
    private readonly MainViewModel vm;
    private bool refreshing;
    private Target? builtSettingsFor;

    public MainWindow(MainViewModel vm)
    {
        this.vm = vm;
        InitializeComponent();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Progress)) UpdateProgress();
            else Refresh();
            if (closeWhenStopped && vm.Phase != Phase.Running) Close();
        };
        Refresh();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        // Started from Explorer's context menu, the window may open behind Explorer; bring it forward.
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        // Coming back from installing ffmpeg or LibreOffice: the warning should be gone without a restart.
        vm.ToolsMayHaveChanged();
    }

    private bool closeWhenStopped;

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing the window while a file is converting means Stop. The app must not exit before the
        // half-written output has been removed, so the window only hides, and closes once that's done.
        if (vm.Phase == Phase.Running)
        {
            e.Cancel = true;
            closeWhenStopped = true;
            vm.Cancel();
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    // State → UI

    private void Refresh()
    {
        refreshing = true;
        try
        {
            TitleText.Text = vm.Title;
            SubtitleText.Text = vm.Subtitle;
            KindGlyph.Text = vm.Glyph;
            var showKind = vm.PrimaryKind != null;
            KindBadge.Visibility = showKind ? Visibility.Visible : Visibility.Collapsed;
            AppIcon.Visibility = showKind ? Visibility.Collapsed : Visibility.Visible;

            var empty = vm.Files.Count == 0;
            var mixed = !empty && vm.Groups.Count == 0;
            DropZone.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            MixedNotice.Visibility = mixed ? Visibility.Visible : Visibility.Collapsed;
            FormatPanel.Visibility = !empty && !mixed && vm.Target != null ? Visibility.Visible : Visibility.Collapsed;

            RefreshFormats();
            RefreshSaveTo();
            RefreshSettings();
            RefreshFooter();
        }
        finally { refreshing = false; }
    }

    private List<string> shownTargetIds = [];

    private void RefreshFormats()
    {
        var items = vm.TargetItems;
        var ids = items.Select(i => i.Target.Id).ToList();
        if (!ids.SequenceEqual(shownTargetIds))
        {
            shownTargetIds = ids;
            var view = new ListCollectionView(items);
            FormatBox.GroupStyle.Clear();
            if (vm.HasSeveralGroups)
            {
                view.GroupDescriptions!.Add(new PropertyGroupDescription(nameof(TargetItem.GroupLabel)));
                FormatBox.GroupStyle.Add(new GroupStyle { HeaderTemplate = GroupHeaderTemplate() });
            }
            FormatBox.ItemsSource = view;
        }
        FormatBox.SelectedItem = items.FirstOrDefault(i => i.Target == vm.Target);
        FormatBox.IsEnabled = vm.Phase == Phase.Idle;
    }

    private static DataTemplate GroupHeaderTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        text.SetValue(TextBlock.FontSizeProperty, 12.0);
        text.SetValue(MarginProperty, new Thickness(10, 8, 10, 4));
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return new DataTemplate { VisualTree = text };
    }

    private void RefreshSettings()
    {
        var t = vm.Target;
        AdvancedExpander.Visibility = t?.HasSettings == true ? Visibility.Visible : Visibility.Collapsed;
        AdvancedExpander.IsExpanded = vm.ShowAdvanced;
        SettingsPanel.IsEnabled = vm.Phase == Phase.Idle;
        if (t == builtSettingsFor) return;
        builtSettingsFor = t;
        SettingsPanel.Children.Clear();
        if (t != null) SettingsBuilder.Build(SettingsPanel, t, vm.Settings, vm.SettingsChanged);
    }

    private void RefreshFooter()
    {
        IdleFooter.Visibility = vm.Phase == Phase.Idle ? Visibility.Visible : Visibility.Collapsed;
        RunningFooter.Visibility = vm.Phase == Phase.Running ? Visibility.Visible : Visibility.Collapsed;
        DoneFooter.Visibility = vm.Phase == Phase.Done ? Visibility.Visible : Visibility.Collapsed;

        // Enter / Esc follow the visible buttons.
        ConvertButton.IsDefault = vm.Phase == Phase.Idle;
        CancelButton.IsCancel = vm.Phase == Phase.Idle;
        StopButton.IsCancel = vm.Phase == Phase.Running;
        DoneButton.IsDefault = DoneButton.IsCancel = vm.Phase == Phase.Done;

        var warning = vm.Warning;
        WarningRow.Visibility = warning != null ? Visibility.Visible : Visibility.Collapsed;
        WarningText.Text = warning ?? "";
        OptionsPanel.Visibility = warning == null && vm.Files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        OpenBox.IsChecked = vm.OpenWhenDone;
        TrashBox.Content = vm.Files.Count > 1 ? "Move originals to Recycle Bin" : "Move original to Recycle Bin";
        TrashBox.IsChecked = vm.TrashOriginals;
        ConvertButton.IsEnabled = vm.CanConvert;

        RunningText.Text = vm.Stopped ? "Stopping…" : $"Converting {vm.CurrentName}…";
        StopButton.IsEnabled = !vm.Stopped;
        UpdateProgress();

        if (vm.Phase == Phase.Done) BuildResults();
        Taskbar.ProgressState = vm.Phase switch
        {
            Phase.Running => vm.Indeterminate ? TaskbarItemProgressState.Indeterminate : TaskbarItemProgressState.Normal,
            Phase.Done when vm.Errors.Count > 0 => TaskbarItemProgressState.Error,
            _ => TaskbarItemProgressState.None,
        };
    }

    private void UpdateProgress()
    {
        Progress.IsIndeterminate = vm.Indeterminate && vm.Phase == Phase.Running;
        Progress.Value = vm.Progress;
        Taskbar.ProgressValue = vm.Progress;
    }

    private void BuildResults()
    {
        ResultLines.Children.Clear();
        if (vm.Converted > 0)
            ResultLines.Children.Add(ResultLine("", $"Converted {vm.Converted} {(vm.Converted == 1 ? "file" : "files")}", "SystemFillColorSuccessBrush"));
        if (vm.SavedElsewhere is { } folder)
            ResultLines.Children.Add(ResultLine("", $"Saved to {OutputFolder.Name(folder)} because the original's folder is read-only"
                                                          + (vm.TrashOriginals ? ". The original stays where it is." : ""),
                                                "TextFillColorSecondaryBrush", small: true));
        if (vm.Trashed > 0)
            ResultLines.Children.Add(ResultLine("", vm.Trashed == 1 ? "Moved the original to the Recycle Bin" : $"Moved {vm.Trashed} originals to the Recycle Bin",
                                                "TextFillColorSecondaryBrush", small: true));
        if (vm.Stopped)
            ResultLines.Children.Add(ResultLine("", "Stopped", "TextFillColorSecondaryBrush"));
        foreach (var e in vm.Errors)
            ResultLines.Children.Add(ResultLine("", e, "SystemFillColorCriticalBrush", small: true));
        ShowButton.IsEnabled = vm.Outputs.Count > 0;
    }

    private static FrameworkElement ResultLine(string glyph, string text, string brush, bool small = false)
    {
        var icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)Application.Current.Resources["IconFont"], Margin = new Thickness(0, 2, 8, 0), FontSize = small ? 12 : 14 };
        icon.SetResourceReference(TextBlock.ForegroundProperty, brush);
        var label = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            TextWrapping = TextWrapping.Wrap,
            FontSize = small ? 12 : 14,
            MinHeight = 0,
            Focusable = true,
        };
        if (small) label.SetResourceReference(ForegroundProperty, brush == "SystemFillColorCriticalBrush" ? brush : "TextFillColorSecondaryBrush");
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(label);
        return row;
    }

    // Events

    private void FormatBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || FormatBox.SelectedItem is not TargetItem item) return;
        vm.Target = item.Target;
    }

    private void Advanced_Toggled(object sender, RoutedEventArgs e)
    {
        if (!refreshing) vm.ShowAdvanced = AdvancedExpander.IsExpanded;
    }

    private void TrashBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!refreshing) vm.TrashOriginals = TrashBox.IsChecked == true;
    }

    private void OpenBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!refreshing) vm.OpenWhenDone = OpenBox.IsChecked == true;
    }

    private async void Convert_Click(object sender, RoutedEventArgs e) => await ConvertAsync(closeWhenDone: false);

    /// <summary>
    /// What the Convert button does. <paramref name="closeWhenDone"/> (Convert to ▸ format): the window
    /// closes by itself a few seconds after everything converted, unless the pointer is on it; it stays
    /// when there is anything to read (an error, Stop, a file saved elsewhere than asked).
    /// </summary>
    public async Task ConvertAsync(bool closeWhenDone)
    {
        if (vm.Phase != Phase.Idle || !vm.CanConvert) return;
        string? folder = null;
        if (vm.SaveTo == SaveMode.Folder) folder = vm.SaveFolder;
        else if (vm.SaveTo == SaveMode.Ask)
        {
            var start = vm.LastAskedFolder is { } last && Directory.Exists(last) ? last : Path.GetDirectoryName(vm.Files.FirstOrDefault());
            var dialog = new OpenFolderDialog { Title = "Save the converted files in" };
            if (start != null) dialog.InitialDirectory = start;
            if (dialog.ShowDialog(this) != true) return;
            folder = vm.LastAskedFolder = dialog.FolderName;
        }
        await vm.StartAsync(folder);
        await DesktopIcons.PlaceAsync(vm.Made);
        if (vm.Converted > 0 && vm.OpenWhenDone) OpenOutputs();
        if (!closeWhenDone) return;
        if (vm.Converted == 0 || vm.Errors.Count > 0 || vm.Stopped || vm.SavedElsewhere != null)
        {
            Log.Write($"quick: stays open (converted {vm.Converted}, errors {vm.Errors.Count}, stopped {vm.Stopped}, saved elsewhere {vm.SavedElsewhere != null})");
            return;
        }
        await Task.Delay(TimeSpan.FromSeconds(3));
        // Not while somebody is reading it: the pointer on the window keeps it open until it moves off.
        while (IsVisible && vm.Phase == Phase.Done && PointerOnWindow()) await Task.Delay(500);
        Log.Write($"quick: done, closing (visible {IsVisible}, phase {vm.Phase})");
        if (IsVisible && vm.Phase == Phase.Done) Close();
    }

    /// <summary>
    /// Where the pointer really is now. WPF's IsMouseOver only changes when the window sees the mouse
    /// move, so it can stay true after the pointer has gone elsewhere.
    /// </summary>
    private bool PointerOnWindow()
    {
        if (!GetCursorPos(out var at)) return false;
        var p = PointFromScreen(new Point(at.X, at.Y));
        return p.X >= 0 && p.Y >= 0 && p.X < ActualWidth && p.Y < ActualHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    /// <summary>Opens the outputs in their usual apps; more than five are shown in Explorer instead.</summary>
    private void OpenOutputs()
    {
        var outputs = vm.Outputs.Where(o => File.Exists(o) || Directory.Exists(o)).ToList();
        if (outputs.Count > 5)
        {
            Explorer.ShowFiles(outputs);
            return;
        }
        foreach (var output in outputs)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(output) { UseShellExecute = true })?.Dispose(); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                Explorer.ShowFiles([output]); // no app for this kind of file
            }
        }
    }

    // Save to: next to the original, a chosen folder (shown by name), "Choose a folder…", or ask each time.

    private const string Beside = "beside", Folder = "folder", Choose = "choose", Ask = "ask";

    private string? shownSaveFolder = "";

    private void RefreshSaveTo()
    {
        if (shownSaveFolder != vm.SaveFolder)
        {
            shownSaveFolder = vm.SaveFolder;
            var list = new List<ComboBoxItem> { new() { Content = "Same folder as the original", Tag = Beside } };
            if (vm.SaveFolder is { } f)
                list.Add(new ComboBoxItem { Content = OutputFolder.Name(f), Tag = Folder, ToolTip = f });
            list.Add(new ComboBoxItem { Content = "Choose a folder…", Tag = Choose });
            list.Add(new ComboBoxItem { Content = "Ask every time", Tag = Ask });
            SaveToBox.ItemsSource = list;
        }
        var items = (List<ComboBoxItem>)SaveToBox.ItemsSource;
        var want = vm.SaveTo switch { SaveMode.Folder => Folder, SaveMode.Ask => Ask, _ => Beside };
        SaveToBox.SelectedItem = items.First(i => (string)i.Tag == want);
        SaveToBox.ToolTip = vm.SaveTo == SaveMode.Folder ? vm.SaveFolder : null;
        SaveToBox.IsEnabled = vm.Phase == Phase.Idle;
    }

    private void SaveToBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || SaveToBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        switch (tag)
        {
            case Beside: vm.SaveTo = SaveMode.Beside; break;
            case Folder: vm.SaveTo = SaveMode.Folder; break;
            case Ask: vm.SaveTo = SaveMode.Ask; break;
            // After the list has closed; Refresh also puts the selection back when the dialog is cancelled.
            case Choose: Dispatcher.InvokeAsync(() => { ChooseSaveFolder(); Refresh(); }); break;
        }
    }

    private void ChooseSaveFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Save converted files in" };
        if (vm.SaveFolder is { } f && Directory.Exists(f)) dialog.InitialDirectory = f;
        if (dialog.ShowDialog(this) != true) return;
        vm.SaveFolder = dialog.FolderName;
        vm.SaveTo = SaveMode.Folder;
    }
    private void Stop_Click(object sender, RoutedEventArgs e) => vm.Cancel();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Show_Click(object sender, RoutedEventArgs e) => Explorer.ShowFiles(vm.Outputs);

    private void ChooseFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Choose files to convert",
            Filter = "Supported files|" + string.Join(";", MediaKinds.AllExtensions.Select(x => "*." + x)) + "|All files|*.*",
        };
        if (dlg.ShowDialog(this) == true) vm.Add(dlg.FileNames);
    }

    // Drag and drop

    private void SetDropHighlight(bool on)
    {
        DropBorder.SetResourceReference(Shape_Stroke, on ? "AccentFillColorDefaultBrush" : "ControlStrongStrokeColorDefaultBrush");
        DropBorder.SetResourceReference(Shape_Fill, on ? "SubtleFillColorSecondaryBrush" : "SubtleFillColorTransparentBrush");
    }

    private static readonly DependencyProperty Shape_Stroke = System.Windows.Shapes.Shape.StrokeProperty;
    private static readonly DependencyProperty Shape_Fill = System.Windows.Shapes.Shape.FillProperty;

    protected override void OnDragEnter(DragEventArgs e) { base.OnDragEnter(e); OnDragOver(e); }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        var ok = e.Data.GetDataPresent(DataFormats.FileDrop) && vm.Phase != Phase.Running;
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        SetDropHighlight(ok);
        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e) { base.OnDragLeave(e); SetDropHighlight(false); }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        SetDropHighlight(false);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            var files = paths.SelectMany(p => Directory.Exists(p)
                ? Directory.EnumerateFiles(p).Where(f => MediaKinds.Of(f) != null)
                : [p]);
            vm.Add(files);
        }
    }
}

internal static class Explorer
{
    /// <summary>Opens Explorer with the outputs selected (one window per folder, like Finder's "Show in Finder").</summary>
    public static void ShowFiles(IEnumerable<string> paths)
    {
        foreach (var group in paths.GroupBy(p => Path.GetDirectoryName(p)!, StringComparer.OrdinalIgnoreCase))
        {
            var folder = ILCreateFromPathW(group.Key);
            if (folder == IntPtr.Zero) continue;
            var items = group.Select(ILCreateFromPathW).Where(p => p != IntPtr.Zero).ToArray();
            try { SHOpenFolderAndSelectItems(folder, (uint)items.Length, items, 0); }
            finally
            {
                foreach (var i in items) ILFree(i);
                ILFree(folder);
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr ILCreateFromPathW(string path);
    [DllImport("shell32.dll")] private static extern void ILFree(IntPtr pidl);
    [DllImport("shell32.dll")] private static extern int SHOpenFolderAndSelectItems(IntPtr folder, uint count, IntPtr[] items, uint flags);
}
