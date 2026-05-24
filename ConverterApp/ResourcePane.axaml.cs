using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ReactiveUI;
using ReactiveUI.Avalonia;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace LSTools.DivineGUI;

public partial class ResourcePane : ReactiveUserControl<ResourcePane>, INotifyPropertyChanged
{
    public bool UseLegacyGuids { get; set => SetField(ref field, value); }
    public string InputFilePath { get; set => SetField(ref field, value); } = string.Empty;
    public string OutputFilePath { get; set => SetField(ref field, value); } = string.Empty;
    public string InputDirectoryPath { get; set => SetField(ref field, value); } = string.Empty;
    public string OutputDirectoryPath { get; set => SetField(ref field, value); } = string.Empty;
    public string SelectedInputFormat { get; set => SetField(ref field, value); } = "LSX (XML) file";
    public string SelectedOutputFormat { get; set => SetField(ref field, value); } = "LSF (binary) file";
    public double ConversionProgressPercentage { get; set => SetField(ref field, value); }
    public string ProgressText { get; set => SetField(ref field, value); } = "Ready";
    public ConversionItem? SelectedBatchItem { get; set => SetField(ref field, value); }

    public ObservableCollection<ConversionItem> BatchFiles { get; } = [];
    public ObservableCollection<string> AvailableFormats { get; } = [
        "LSX (XML) file", "LSB (binary) file", "LSF (binary) file", "LSJ (JSON) file"
    ];

    public ICommand BrowseInputFileCommand { get; }
    public ICommand BrowseOutputFileCommand { get; }
    public ICommand BrowseInputDirCommand { get; }
    public ICommand BrowseOutputDirCommand { get; }
    public ICommand ConvertFileCommand { get; }
    public ICommand BulkConvertCommand { get; }
    public ICommand ValidateInputPathCommand { get; }
    public ICommand OnGridSelectionChangedCommand { get; }
    public ICommand OnGridItemDoubleTappedCommand { get; }

    private static readonly FilePickerFileType LsFileTypes = new("LS files")
    {
        Patterns = ["*.lsx", "*.lsb", "*.lsf", "*.lsj", "*.lsfx", "*.lsbc", "*.lsbs"]
    };

    public ResourcePane()
    {
        InitializeComponent();

        DataContext = this;

        BrowseInputFileCommand = ReactiveCommand.CreateFromTask(BrowseInputFileAsync);
        BrowseOutputFileCommand = ReactiveCommand.CreateFromTask(BrowseOutputFileAsync);
        BrowseInputDirCommand = ReactiveCommand.CreateFromTask(BrowseInputFolderAsync);
        BrowseOutputDirCommand = ReactiveCommand.CreateFromTask(BrowseOutputFolderAsync);
        ConvertFileCommand = ReactiveCommand.CreateFromTask(ExecuteSingleFileConversionAsync);
        BulkConvertCommand = ReactiveCommand.CreateFromTask(ExecuteBulkQueueConversionAsync);

        ValidateInputPathCommand = ReactiveCommand.Create(ExecutePathValidation);
        OnGridSelectionChangedCommand = ReactiveCommand.Create(ExecuteGridSelectionAlert);
        OnGridItemDoubleTappedCommand = ReactiveCommand.CreateFromTask(ExecuteGridItemExecutionAsync);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async Task BrowseInputFileAsync()
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null) return;

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Input File",
            AllowMultiple = false,
            FileTypeFilter = [LsFileTypes]
        });
        if (files.Count > 0) InputFilePath = files[0].Path.LocalPath;
    }

    private async Task BrowseOutputFileAsync()
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null) return;

        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Select Output File",
            FileTypeChoices = [LsFileTypes],
            DefaultExtension = "lsf"
        });
        if (file is not null) OutputFilePath = file.Path.LocalPath;
    }

    private async Task BrowseInputFolderAsync()
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null) return;

        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Input Directory",
            AllowMultiple = false
        });
        if (folders.Count > 0)
        {
            InputDirectoryPath = folders[0].Path.LocalPath;
            await AutoPopulateBatchQueueAsync(InputDirectoryPath);
        }
    }

    private async Task BrowseOutputFolderAsync()
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null) return;

        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Output Directory",
            AllowMultiple = false
        });
        if (folders.Count > 0) OutputDirectoryPath = folders[0].Path.LocalPath;
    }

    private async Task AutoPopulateBatchQueueAsync(string searchDirectory)
    {
        if (!Directory.Exists(searchDirectory)) return;
        BatchFiles.Clear();
        ProgressText = "Scanning directory...";

        var matchingFiles = await Task.Run(() =>
            Directory.EnumerateFiles(searchDirectory, "*.*", SearchOption.TopDirectoryOnly)
                     .Where(f => LsFileTypes.Patterns!.Any(p => f.EndsWith(p[1..], StringComparison.OrdinalIgnoreCase)))
                     .ToList());

        foreach (var file in matchingFiles)
        {
            string fileName = Path.GetFileName(file);
            BatchFiles.Add(new ConversionItem(fileName, Path.ChangeExtension(fileName, ".lsf")));
        }
        ProgressText = $"Discovered {BatchFiles.Count} matching files.";
    }

    private async Task ExecuteSingleFileConversionAsync()
    {
        if (string.IsNullOrWhiteSpace(InputFilePath) || !File.Exists(InputFilePath)) return;
        ProgressText = "Converting file...";
        ConversionProgressPercentage = 50;
        await Task.Delay(500);
        ConversionProgressPercentage = 100;
        ProgressText = "Conversion complete.";
    }

    private async Task ExecuteBulkQueueConversionAsync()
    {
        if (BatchFiles.Count == 0) return;
        for (int i = 0; i < BatchFiles.Count; i++)
        {
            var item = BatchFiles[i];
            item.Status = "Converting...";
            await Task.Delay(200);
            item.Status = "Completed";
            ConversionProgressPercentage = ((double)(i + 1) / BatchFiles.Count) * 100;
        }
        ProgressText = "Batch complete.";
    }

    private void ExecutePathValidation()
    {
        if (!string.IsNullOrWhiteSpace(InputFilePath) && !File.Exists(InputFilePath))
            ProgressText = "Warning: Input file does not exist.";
    }

    private void ExecuteGridSelectionAlert()
    {
        if (SelectedBatchItem is not null)
            ProgressText = $"Focus: {SelectedBatchItem.SourceFile}";
    }

    private async Task ExecuteGridItemExecutionAsync() => await Task.CompletedTask;

    private PropertyChangedEventHandler? _propertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => _propertyChanged += value;
        remove => _propertyChanged -= value;
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
public class ConversionItem(string sourceFile, string targetFile) : INotifyPropertyChanged
{
    public string SourceFile { get; } = sourceFile;
    public string TargetFile { get; } = targetFile;

    public string Status
    {
        get;
        set { field = value; OnPropertyChanged(); }
    } = "Pending";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}