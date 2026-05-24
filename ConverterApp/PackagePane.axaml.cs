using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LSLib.LS;
using LSLib.LS.Enums;
using ReactiveUI;
using ReactiveUI.Avalonia;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive.Concurrency;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace LSTools.DivineGUI;

public class PackageFileQueueItem : INotifyPropertyChanged
{
    private string _relativePath = string.Empty;
    private long _size;
    private string _method = "None";
    private string _status = "Pending";

    public string RelativePath { get => _relativePath; set { _relativePath = value; OnPropertyChanged(nameof(RelativePath)); } }
    public long Size { get => _size; set { _size = value; OnPropertyChanged(nameof(Size)); } }
    public string Method { get => _method; set { _method = value; OnPropertyChanged(nameof(Method)); } }
    public string Status { get => _status; set { _status = value; OnPropertyChanged(nameof(Status)); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class PackagePane : ReactiveUserControl<PackagePane>, INotifyPropertyChanged
{
    // --- STABLE EXPLICIT BACKING FIELDS ---
    private string _extractPackagePath = string.Empty;
    private string _extractionPath = string.Empty;
    private string _createSrcPath = string.Empty;
    private string _createPackagePath = string.Empty;
    private decimal _packagePriority = 0;
    private bool _isSolid;
    private bool _preloadIntoCache;
    private bool _allowMemoryMapping;
    private string _selectedVersion = "V18 (Baldur's Gate 3 Release)";
    private string _selectedCompression = "LZ4";
    private string _packageProgressLabel = "Ready";
    private double _packageProgressValue;

    // FIX: Added the missing public property explicitly to satisfy Line 63 of your XAML DataGrid
    public ObservableCollection<PackageFileQueueItem> PackageFilesQueue { get; } = [];

    public string ExtractPackagePath { get => _extractPackagePath; set { if (_extractPackagePath != value) { _extractPackagePath = value; InvokeLocalProperty(nameof(ExtractPackagePath)); } } }
    public string ExtractionPath { get => _extractionPath; set { if (_extractionPath != value) { _extractionPath = value; InvokeLocalProperty(nameof(ExtractionPath)); } } }
    public string CreateSrcPath { get => _createSrcPath; set { if (_createSrcPath != value) { _createSrcPath = value; InvokeLocalProperty(nameof(CreateSrcPath)); } } }

    public string CreatePackagePath
    {
        get => _createPackagePath;
        set
        {
            if (_createPackagePath != value)
            {
                _createPackagePath = value;
                InvokeLocalProperty(nameof(CreatePackagePath));
                if (!string.IsNullOrEmpty(value) && Path.GetExtension(value).Equals(".lsv", StringComparison.OrdinalIgnoreCase))
                {
                    SelectedCompression = "Zlib Optimal";
                }
            }
        }
    }

    public decimal PackagePriority { get => _packagePriority; set { if (_packagePriority != value) { _packagePriority = value; InvokeLocalProperty(nameof(PackagePriority)); } } }
    public bool IsSolid { get => _isSolid; set { if (_isSolid != value) { _isSolid = value; InvokeLocalProperty(nameof(IsSolid)); } } }
    public bool PreloadIntoCache { get => _preloadIntoCache; set { if (_preloadIntoCache != value) { _preloadIntoCache = value; InvokeLocalProperty(nameof(PreloadIntoCache)); } } }
    public bool AllowMemoryMapping { get => _allowMemoryMapping; set { if (_allowMemoryMapping != value) { _allowMemoryMapping = value; InvokeLocalProperty(nameof(AllowMemoryMapping)); } } }
    public string SelectedVersion { get => _selectedVersion; set { if (_selectedVersion != value) { _selectedVersion = value; InvokeLocalProperty(nameof(SelectedVersion)); } } }
    public string SelectedCompression { get => _selectedCompression; set { if (_selectedCompression != value) { _selectedCompression = value; InvokeLocalProperty(nameof(SelectedCompression)); } } }
    public string PackageProgressLabel { get => _packageProgressLabel; set { if (_packageProgressLabel != value) { _packageProgressLabel = value; InvokeLocalProperty(nameof(PackageProgressLabel)); } } }
    public double PackageProgressValue { get => _packageProgressValue; set { if (_packageProgressValue != value) { _packageProgressValue = value; InvokeLocalProperty(nameof(PackageProgressValue)); } } }

    public ObservableCollection<string> AvailableVersions { get; } = [
        "V18 (Baldur's Gate 3 Release)",
        "V13 (Divinity Original Sin: EE, Original Sin 2)",
        "V10 (Divinity Original Sin)",
        "V9 (Divinity Original Sin Classic)",
        "V7 (Divinity Original Sin Classic - Old)"
    ];

    public ObservableCollection<string> AvailableCompressions { get; } = [
        "No compression", "Zlib Fast", "Zlib Optimal", "LZ4", "LZ4 HC", "ZStd Fast", "ZStd Optimal", "ZStd Max"
    ];

    public ICommand BrowseExtractPackageCommand { get; }
    public ICommand BrowseExtractFolderCommand { get; }
    public ICommand BrowseCreateSrcCommand { get; }
    public ICommand BrowseCreatePackageTargetCommand { get; }
    public ICommand ExtractPackageCommand { get; }
    public ICommand CreatePackageCommand { get; }
    public ICommand ValidatePackagePathCommand { get; }

    private static readonly FilePickerFileType PakFileTypes = new("LS package / savegame files") { Patterns = ["*.pak", "*.lsv"] };

    public PackagePane()
    {
        InitializeComponent();
        DataContext = this;
        ViewModel = this;

        BrowseExtractPackageCommand = ReactiveCommand.CreateFromTask(BrowseExtractPackageAsync);
        BrowseExtractFolderCommand = ReactiveCommand.CreateFromTask(BrowseExtractFolderAsync);
        BrowseCreateSrcCommand = ReactiveCommand.CreateFromTask(BrowseCreateSrcAsync);
        BrowseCreatePackageTargetCommand = ReactiveCommand.CreateFromTask(BrowseCreatePackageTargetAsync);

        ExtractPackageCommand = ReactiveCommand.CreateFromTask(ExecuteExtractionAsync);
        CreatePackageCommand = ReactiveCommand.CreateFromTask(ExecutePackageCreationAsync);
        ValidatePackagePathCommand = ReactiveCommand.Create(ExecutePackagePathValidation);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async Task<string> ResolveOpenFileOrFolderAsync<TItem>(Func<IStorageProvider, Task<IReadOnlyList<TItem>>> pickerFunc) where TItem : IStorageItem
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null) return string.Empty;

        var results = await pickerFunc(provider);
        return results.Count > 0 ? results[0].Path.LocalPath : string.Empty;
    }

    private async Task BrowseExtractPackageAsync() =>
        ExtractPackagePath = await ResolveOpenFileOrFolderAsync(p => p.OpenFilePickerAsync(new() { Title = "Select Package to Extract", AllowMultiple = false, FileTypeFilter = [PakFileTypes] }));

    private async Task BrowseExtractFolderAsync() =>
        ExtractionPath = await ResolveOpenFileOrFolderAsync(p => p.OpenFolderPickerAsync(new() { Title = "Select Extraction Destination", AllowMultiple = false }));

    private async Task BrowseCreateSrcAsync() =>
        CreateSrcPath = await ResolveOpenFileOrFolderAsync(p => p.OpenFolderPickerAsync(new() { Title = "Select Folder to Pack", AllowMultiple = false }));

    private async Task BrowseCreatePackageTargetAsync()
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null) return;

        var file = await provider.SaveFilePickerAsync(new() { Title = "Select Output Package Destination", FileTypeChoices = [PakFileTypes], DefaultExtension = "pak" });
        if (file is not null) CreatePackagePath = file.Path.LocalPath;
    }

    private void ExecutePackagePathValidation()
    {
        if (!string.IsNullOrWhiteSpace(CreatePackagePath) && !CreatePackagePath.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) && !CreatePackagePath.EndsWith(".lsv", StringComparison.OrdinalIgnoreCase))
        {
            PackageProgressLabel = "Warning: Target file extension should typically be .pak or .lsv";
        }
    }

    private async Task ExecuteExtractionAsync()
    {
        if (string.IsNullOrWhiteSpace(ExtractPackagePath) || !File.Exists(ExtractPackagePath))
        {
            PackageProgressLabel = "Error: Input archive target missing or inaccessible.";
            return;
        }

        PackageProgressLabel = "Unpacking package layers... 0%";
        PackageProgressValue = 0;
        PackageFilesQueue.Clear();
        int lastReportedPercent = -1;

        try
        {
            string input = ExtractPackagePath;
            string output = ExtractionPath;

            await Task.Run(() =>
            {
                // FIX: Initialized the instance reader using an object reference to avoid static constraint errors
                var packageReader = new PackageReader();
                var pkg = packageReader.Read(input);

                foreach (var file in pkg.Files)
            {
                RxSchedulers.MainThreadScheduler.Schedule(() => PackageFilesQueue.Add(new PackageFileQueueItem
                {
                    RelativePath = file.Name,
                    Size = (long)file.Size(),
                    // FIX: Substituted .Method with .Flags evaluation string to resolve the missing property definition
                    Method = file.Flags.ToString(),
                    Status = "Extracted"
                }));
            }
                var packager = new Packager();
                packager.ProgressUpdate += (status, numerator, denominator) =>
                {
                    double precisePercent = denominator == 0 ? 0 : (double)numerator * 100 / denominator;
                    int currentPercent = (int)Math.Floor(precisePercent);
                    if (currentPercent == lastReportedPercent && numerator != 0 && numerator != denominator) return;
                    lastReportedPercent = currentPercent;
                    RxSchedulers.MainThreadScheduler.Schedule(() =>
                    {
                        PackageProgressLabel = $"{status} ({currentPercent}%)";
                        PackageProgressValue = precisePercent;
                    });
                };
                packager.UncompressPackage(input, output);
            });
            PackageProgressLabel = "Package decompressed and extracted successfully.";
            PackageProgressValue = 100;
        }
        catch (Exception exc)
        {
            PackageProgressLabel = $"Fault: {exc.Message}";
        }
    }
    private async Task ExecutePackageCreationAsync()
    {
        if (string.IsNullOrWhiteSpace(CreateSrcPath) || !Directory.Exists(CreateSrcPath))
        {
            PackageProgressLabel = "Error: Invalid source folder specified.";
            return;
        }
        PackageProgressLabel = "Initializing build data structure...";
        PackageProgressValue = 0;
        int lastReportedPercent = -1;
        try
        {
            var build = new PackageBuildData
            {
                Version = SelectedVersion switch
                {
                    string v when v.StartsWith("V18") => PackageVersion.V18,
                    string v when v.StartsWith("V13") => PackageVersion.V13,
                    string v when v.StartsWith("V10") => PackageVersion.V10,
                    string v when v.StartsWith("V9") => PackageVersion.V9,
                    string v when v.StartsWith("V7") => PackageVersion.V7,
                    _ => PackageVersion.V18
                },
                Priority = (byte)PackagePriority
            };
            (build.Compression, build.CompressionLevel) = SelectedCompression switch
            {
                "Zlib Fast" => (CompressionMethod.Zlib, LSCompressionLevel.Fast),
                "Zlib Optimal" => (CompressionMethod.Zlib, LSCompressionLevel.Default),
                "LZ4 Fast" => (CompressionMethod.LZ4, LSCompressionLevel.Fast),
                "LZ4" => (CompressionMethod.LZ4, LSCompressionLevel.Default),
                "ZStd Fast" => (CompressionMethod.Zstd, LSCompressionLevel.Fast),
                "ZStd Optimal" => (CompressionMethod.Zstd, LSCompressionLevel.Default),
                "ZStd Max" => (CompressionMethod.Zstd, LSCompressionLevel.Max),
                _ => (CompressionMethod.None, LSCompressionLevel.Default)
            };
            if (build.Compression == CompressionMethod.LZ4 && build.Version <= PackageVersion.V9)
            {
                build.Compression = CompressionMethod.Zlib;
            }
            if (IsSolid) build.Flags |= PackageFlags.Solid;
            if (AllowMemoryMapping) build.Flags |= PackageFlags.AllowMemoryMapping;
            if (PreloadIntoCache) build.Flags |= PackageFlags.Preload;
            string srcDir = CreateSrcPath;
            string destFile = CreatePackagePath;
            var packager = new Packager();
            packager.ProgressUpdate += (status, numerator, denominator) =>
            {
                double precisePercent = denominator == 0 ? 0 : (double)numerator * 100 / denominator;
                int currentPercent = (int)Math.Floor(precisePercent);
                if (currentPercent == lastReportedPercent && numerator != 0 && numerator != denominator) return;
                lastReportedPercent = currentPercent;
                RxSchedulers.MainThreadScheduler.Schedule(() =>
                {
                    PackageProgressLabel = $"{status} ({currentPercent}%)";
                    PackageProgressValue = precisePercent;
                });
            };
            await Task.Run(() => packager.CreatePackage(destFile, srcDir, build));
            PackageProgressLabel = "New archive file package constructed successfully.";
            PackageProgressValue = 100;
        }
        catch (Exception exc)
        {
            PackageProgressLabel = $"Build Failed: {exc.Message}";
            PackageProgressValue = 0;
        }
    }
    private PropertyChangedEventHandler? _localPropertyChanged;
    public new event PropertyChangedEventHandler? PropertyChanged
    {
        add => _localPropertyChanged += value;
        remove => _localPropertyChanged -= value;
    }
    private void InvokeLocalProperty(string propertyName) =>
    _localPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}