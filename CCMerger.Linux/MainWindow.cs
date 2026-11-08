// Main window. Same controls and texts as the WinForms version (Form1), laid out in code
// instead of XAML to keep the project to a handful of plain C# files.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using CCMerger.Dbpf;

namespace CCMerger;

public sealed class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();

    private readonly TextBox _dirBox;
    private readonly Button _browseButton;
    private readonly NumericUpDown _sizeBox;
    private readonly NumericUpDown _filesBox;
    private readonly ProgressBar _progressBar;
    private readonly TextBlock _progressText;
    private readonly Button _mergeButton;
    private readonly CheckBox _logBox;

    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _closed;

    public MainWindow()
    {
        Title = AppInfo.Title;
        Width = 700;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Background = App.WindowBackground;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        TrySetIcon();

        string folder = _settings.Folder;
        if (string.IsNullOrWhiteSpace(folder))
            folder = AppSettings.FindDefaultFolder();

        // ---- row 0: folder ----
        _dirBox = new TextBox { Text = folder };
        _browseButton = new Button { Content = "Browse" };
        var folderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var folderLabel = MakeLabel("Directory with CC to merge:");
        folderLabel.Margin = new Thickness(0, 0, 8, 0);
        _browseButton.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_dirBox, 1);
        Grid.SetColumn(_browseButton, 2);
        folderRow.Children.Add(folderLabel);
        folderRow.Children.Add(_dirBox);
        folderRow.Children.Add(_browseButton);

        // ---- rows 1 and 2: limits ----
        _sizeBox = Number(1000, _settings.MaxPackageSizeMb);
        _filesBox = Number(8000, _settings.MaxFilesPerPackage);
        var sizeRow = LimitRow("Max package size (MB)", _sizeBox, "Default: 100");
        var filesRow = LimitRow("Max files per package", _filesBox, "Default: 1000");

        var hint = new TextBlock
        {
            Text = "(If your game crashes with higher values, use the default ones or try smaller.)",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        // ---- rows 4 and 5: progress + log checkbox ----
        _progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 28 };
        _progressText = new TextBlock
        {
            FontSize = 16,
            MinWidth = 120,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var progressRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(_progressText, 1);
        progressRow.Children.Add(_progressBar);
        progressRow.Children.Add(_progressText);

        _logBox = new CheckBox
        {
            Content = "Write the contents of each merged package in text files",
            IsChecked = _settings.WriteContentLists
        };

        var left = new StackPanel { Spacing = 8 };
        left.Children.Add(folderRow);
        left.Children.Add(sizeRow);
        left.Children.Add(filesRow);
        left.Children.Add(hint);
        left.Children.Add(progressRow);
        left.Children.Add(_logBox);

        _mergeButton = new Button
        {
            Content = "Merge",
            Width = 96,
            Height = 56,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        var root = new Grid
        {
            Margin = new Thickness(16),
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        Grid.SetColumn(_mergeButton, 1);
        root.Children.Add(left);
        root.Children.Add(_mergeButton);
        Content = root;

        _browseButton.Click += async (_, _) => await BrowseAsync();
        _mergeButton.Click += async (_, _) => await MergeAsync();
    }

    // ---------------------------------------------------------------------------------
    // small layout helpers

    private static TextBlock MakeLabel(string text) =>
        new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };

    private static NumericUpDown Number(int max, int value) =>
        new NumericUpDown
        {
            Minimum = 0,
            Maximum = max,
            Increment = 1,
            FormatString = "0",
            Width = 160,
            Value = Math.Min(max, Math.Max(0, value))
        };

    private static Grid LimitRow(string label, NumericUpDown box, string defaultText)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("200,Auto,Auto") };
        var defaultLabel = MakeLabel(defaultText);
        defaultLabel.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(box, 1);
        Grid.SetColumn(defaultLabel, 2);
        row.Children.Add(MakeLabel(label));
        row.Children.Add(box);
        row.Children.Add(defaultLabel);
        return row;
    }

    private void TrySetIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://CCMerger/Assets/ccmerger.png"));
            Icon = new WindowIcon(stream);
        }
        catch
        {
            // no icon is not a reason to fail
        }
    }

    // ---------------------------------------------------------------------------------
    // events

    private async Task BrowseAsync()
    {
        try
        {
            IStorageFolder? start = null;
            string current = _dirBox.Text?.Trim() ?? "";
            if (Directory.Exists(current))
                start = await StorageProvider.TryGetFolderFromPathAsync(current);

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Directory with CC to merge",
                AllowMultiple = false,
                SuggestedStartLocation = start
            });
            if (folders.Count == 0)
                return;

            string? path = folders[0].TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                _dirBox.Text = path;
                SaveSettings();
            }
        }
        catch (Exception ex)
        {
            await MessageDialog.ShowAsync(this, "Error",
                "Couldn't open the folder picker. You can type or paste the folder path in the text box instead.\n\n" + ex.Message);
        }
    }

    private async Task<string?> PickTargetAsync(string dir)
    {
        try
        {
            IStorageFolder? start = await StorageProvider.TryGetFolderFromPathAsync(dir);
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save merged packages as",
                SuggestedFileName = "mergedcc.package",
                DefaultExtension = "package",
                ShowOverwritePrompt = true,
                SuggestedStartLocation = start,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Package Files") { Patterns = new[] { "*.package" } }
                }
            });
            return file?.TryGetLocalPath();
        }
        catch (Exception ex)
        {
            await MessageDialog.ShowAsync(this, "Warning",
                "Couldn't open the save dialog (" + ex.Message + ").\n\n" +
                "The merged packages will be saved in the CC folder as mergedcc0.package, mergedcc1.package...");
            return Path.Combine(dir, "mergedcc.package");
        }
    }

    private async Task MergeAsync()
    {
        if (_busy)
            return;

        string dir = _dirBox.Text?.Trim() ?? "";
        if (!Directory.Exists(dir))
        {
            await MessageDialog.ShowAsync(this, "Error", "Invalid directory");
            return;
        }

        string? target = await PickTargetAsync(dir);
        if (string.IsNullOrWhiteSpace(target))
            return;

        SaveSettings();
        SetBusy(true);

        var options = new MergeOptions
        {
            SourceDirectory = dir,
            TargetPath = target,
            MaxPackageBytes = (long)(_sizeBox.Value ?? 0) * 1000000L,
            MaxFilesPerPackage = (uint)(_filesBox.Value ?? 0),
            WriteContentLists = _logBox.IsChecked == true
        };

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var progress = new Progress<MergeProgress>(OnProgress);

        MergeResult? result = null;
        Exception? crash = null;
        try
        {
            result = await Task.Run(() => Merger.Run(options, progress, token));
        }
        catch (OperationCanceledException)
        {
            // window closed while merging
        }
        catch (Exception ex)
        {
            crash = ex;
        }

        SetBusy(false);
        if (_closed)
            return;

        if (crash != null)
        {
            await MessageDialog.ShowAsync(this, "Error",
                "There was an error writing the merged packages:\n\n" + crash);
        }
        else if (result == null)
        {
            // cancelled
        }
        else if (result.NothingToMerge)
        {
            string text = "There are no packages to merge in here, or they're all empty!";
            if (result.Errors.Count > 0)
                text += "\n\n" + result.Errors.Count + " packages couldn't be read. Check the log at CCMerger.log in the output directory for more details.";
            await MessageDialog.ShowAsync(this, "Error", text);
        }
        else if (result.Failed)
        {
            await MessageDialog.ShowAsync(this, "Error",
                "Failed to merge the packages :(. Check the log at CCMerger.log in the output directory for more details.");
        }
        else if (result.Errors.Count > 0)
        {
            await MessageDialog.ShowAsync(this, "Finished",
                "There were errors merging " + result.Errors.Count + " packages. Check the log at CCMerger.log in the output directory for more details.");
        }
        else
        {
            await MessageDialog.ShowAsync(this, "Finished", "Packages merged successfully");
        }
    }

    private void OnProgress(MergeProgress p)
    {
        if (!_busy)
            return;

        if (p.Preparing)
        {
            _progressBar.Value = 0;
            _progressText.Text = "Preparing...";
        }
        else
        {
            _progressBar.Value = p.Percent;
            _progressText.Text = p.Percent + "%";
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _browseButton.IsEnabled = !busy;
        _dirBox.IsEnabled = !busy;
        _mergeButton.IsEnabled = !busy;
        _sizeBox.IsEnabled = !busy;
        _filesBox.IsEnabled = !busy;
        _logBox.IsEnabled = !busy;

        _progressBar.Value = 0;
        _progressText.Text = busy ? "Preparing..." : "";
    }

    private void SaveSettings()
    {
        _settings.Folder = _dirBox.Text?.Trim() ?? "";
        _settings.MaxPackageSizeMb = (int)(_sizeBox.Value ?? 0);
        _settings.MaxFilesPerPackage = (int)(_filesBox.Value ?? 0);
        _settings.WriteContentLists = _logBox.IsChecked == true;
        _settings.Save();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _closed = true;
        SaveSettings();
        _cts?.Cancel();
        base.OnClosing(e);
    }
}
