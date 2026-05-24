using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LSLib.VirtualTextures;
using ReactiveUI;
using ReactiveUI.Avalonia;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive.Concurrency;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace LSTools.DivineGUI;

public partial class VirtualTexturesPane : ReactiveUserControl<VirtualTexturesPane>, INotifyPropertyChanged
{
    public ObservableCollection<VirtualTextureGridItem> TexturesList { get; } = [];

    public string GtsPath { get; set => SetField(ref field, value); } = string.Empty;
    public string DestinationPath { get; set => SetField(ref field, value); } = string.Empty;
    public string GTexNameInput { get; set => SetField(ref field, value); } = string.Empty;
    public string TileSetConfigPath { get; set => SetField(ref field, value); } = string.Empty;
    public string ModRootPath { get; set => SetField(ref field, value); } = string.Empty;
    public bool FastBuild { get; set => SetField(ref field, value); }
    public string ActionProgressLabel { get; set => SetField(ref field, value); } = "Ready";
    public double ActionProgressValue { get; set => SetField(ref field, value); }

    public ICommand BrowseGtsCommand { get; }
    public ICommand BrowseDestinationCommand { get; }
    public ICommand BrowseConfigCommand { get; }
    public ICommand BrowseModRootCommand { get; }
    public ICommand ExtractTileSetCommand { get; }
    public ICommand BuildTileSetCommand { get; }

    private static readonly FilePickerFileType GtsFileType = new("Virtual Texture Set")
    {
        Patterns = ["*.gts"]
    };

    private static readonly FilePickerFileType XmlConfigFileType = new("Virtual Texture Set Configuration")
    {
        Patterns = ["*.xml"]
    };

    private int ActionProgressMaximum = 100;

    public VirtualTexturesPane()
    {
        InitializeComponent();

        DataContext = this;
        ViewModel = this;

        BrowseGtsCommand = ReactiveCommand.CreateFromTask(BrowseGtsAsync);
        BrowseDestinationCommand = ReactiveCommand.CreateFromTask(BrowseDestinationAsync);
        BrowseConfigCommand = ReactiveCommand.CreateFromTask(BrowseConfigAsync);
        BrowseModRootCommand = ReactiveCommand.CreateFromTask(BrowseModRootAsync);

        ExtractTileSetCommand = ReactiveCommand.CreateFromTask(ExecuteExtractionAsync);
        BuildTileSetCommand = ReactiveCommand.CreateFromTask(ExecuteBuildAsync);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async Task<string> ResolvePathAsync<T>(Func<IStorageProvider, Task<IReadOnlyList<T>>> pickerFunc) where T : IStorageItem
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null) return string.Empty;

        var results = await pickerFunc(provider);
        return results.Count > 0 ? results[0].Path.LocalPath : string.Empty;
    }

    private async Task BrowseGtsAsync() =>
        GtsPath = await ResolvePathAsync(p => p.OpenFilePickerAsync(new() { Title = "Select GTS File", AllowMultiple = false, FileTypeFilter = [GtsFileType] }));

    private async Task BrowseDestinationAsync() =>
        DestinationPath = await ResolvePathAsync(p => p.OpenFolderPickerAsync(new() { Title = "Select Destination Folder", AllowMultiple = false }));

    private async Task BrowseConfigAsync() =>
        TileSetConfigPath = await ResolvePathAsync(p => p.OpenFilePickerAsync(new() { Title = "Select Configuration File", AllowMultiple = false, FileTypeFilter = [XmlConfigFileType] }));

    private async Task BrowseModRootAsync() =>
         ModRootPath = await ResolvePathAsync(p => p.OpenFolderPickerAsync(new() { Title = "Select Mod Root Folder", AllowMultiple = false }));

    private async Task ExecuteExtractionAsync()
    {
        if (string.IsNullOrWhiteSpace(GtsPath) || !File.Exists(GtsPath))
        {
            ActionProgressLabel = "Error: Invalid target configuration settings.";
            return;
        }

        ActionProgressLabel = "Extracting virtual textures...";
        ActionProgressValue = 10;

        RxSchedulers.MainThreadScheduler.Schedule(() => TexturesList.Clear());

        await Task.Run(async () =>
        {
            try
            {
                var tileSet = new VirtualTileSet(GtsPath);
                var rawTextures = tileSet.ExtractTextureMetadata();
                var texName = GTexNameInput.Trim();

                List<VirtualTextureInfo> texturesToProcess = [];
                if (texName.Length > 0)
                {
                    for (int idx = 0; idx < rawTextures.Count; idx++)
                    {
                        if (rawTextures[idx].Name.Equals(texName, StringComparison.OrdinalIgnoreCase))
                        {
                            texturesToProcess.Add(rawTextures[idx]);
                        }
                    }

                    if (texturesToProcess.Count == 0)
                    {
                        RxSchedulers.MainThreadScheduler.Schedule(() => {
                            ActionProgressLabel = $"Extraction Failed: {GTexNameInput} was not found in this tile set.";
                        });
                        return;
                    }
                }
                else
                {
                    texturesToProcess = rawTextures;
                }

                for (int i = 0; i < texturesToProcess.Count; i++)
                {
                    var texture = texturesToProcess[i];
                    double currentPercent = (double)i * 100 / texturesToProcess.Count;

                    RxSchedulers.MainThreadScheduler.Schedule(() => {
                        ActionProgressLabel = $"Extracting GTex: {texture.Name}";
                        ActionProgressValue = currentPercent;

                        TexturesList.Add(new VirtualTextureGridItem
                        {
                            Name = texture.Name,
                            Format = "DDS Layer Extract",
                            Dimensions = $"{texture.Width}x{texture.Height}",
                            Status = "Processing"
                        });
                    });

                    for (var layer = 0; layer < tileSet.TileSetLayers.Length; layer++)
                    {
                        BC3Image? tex = null;
                        var level = 0;
                        do
                        {
                            tex = tileSet.ExtractTexture(level, layer, texture);
                            level++;
                        } while (tex == null && level < tileSet.TileSetLevels.Length);

                        if (tex != null)
                        {
                            var outputPath = Path.Join(DestinationPath, texture.Name + $"_{layer}.dds");
                            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                            using var bw = new BinaryWriter(fs);
                            tex.WriteDDS(bw);
                        }
                    }

                    int currentIdx = i;
                    RxSchedulers.MainThreadScheduler.Schedule(() => {
                        if (currentIdx < TexturesList.Count) TexturesList[currentIdx].Status = "Success";
                    });

                    tileSet.ReleasePageFiles();
                    GC.Collect();
                }

                RxSchedulers.MainThreadScheduler.Schedule(() => {
                    ActionProgressValue = 100;
                    ActionProgressLabel = "Virtual textures extraction completed successfully.";
                });
            }
            catch (Exception exc)
            {
                RxSchedulers.MainThreadScheduler.Schedule(() => {
                    ActionProgressLabel = $"Extraction Failed: Internal error! {exc.Message}";
                });
            }
        });
    }

    private async Task ExecuteBuildAsync()
    {
        if (string.IsNullOrWhiteSpace(TileSetConfigPath) || !File.Exists(TileSetConfigPath))
        {
            ActionProgressLabel = "Error: Configuration reference parameter file not found.";
            return;
        }

        ActionProgressLabel = FastBuild ? "Compiling via Fast Build compiler paths..." : "Analyzing deep tile configurations...";
        ActionProgressValue = 30;

        await Task.Run(async () =>
        {
            try
            {
                var descriptor = new TileSetDescriptor
                {
                    RootPath = ModRootPath,
                    Config = { FastBuild = FastBuild }
                };
                descriptor.Load(TileSetConfigPath);

                var builder = new TileSetBuilder(descriptor.Config);

                builder.OnStepStarted += (step) => RxSchedulers.MainThreadScheduler.Schedule(() => ActionProgressLabel = step);
                builder.OnStepProgress += (numerator, denumerator) =>
                {
                    RxSchedulers.MainThreadScheduler.Schedule(() => {
                        ActionProgressMaximum = denumerator;
                        ActionProgressValue = denumerator == 0 ? 0 : (double)numerator * 100 / denumerator;
                    });
                };

                builder.OnStepStarted("Adding textures");
                var texturesSpan = CollectionsMarshal.AsSpan(descriptor.Textures);
                for (int i = 0; i < texturesSpan.Length; i++)
                {
                    var texture = texturesSpan[i];
                    List<string> layerPaths = [];
                    var layersSpan = CollectionsMarshal.AsSpan(texture.Layers);
                    for (int l = 0; l < layersSpan.Length; l++)
                    {
                        var name = layersSpan[l];
                        layerPaths.Add(name is not null ? Path.Combine(descriptor.SourceTexturePath, name) : string.Empty);
                    }
                    builder.AddTexture(texture.Name, layerPaths);
                }
                builder.OnStepStarted("Dividing textures into virtual tiers and writing page files...");
                builder.TileSet = new VirtualTileSet();
                var targetGtpDirectory = Path.GetDirectoryName(descriptor.VirtualTexturePath) ?? descriptor.RootPath;
                builder.OnStepStarted("\nWriting master metadata (.gts) container definition...");
                builder.TileSet?.Save(descriptor.VirtualTexturePath);
                RxSchedulers.MainThreadScheduler.Schedule(() => {
                    ActionProgressValue = 100;
                    ActionProgressLabel = "Tile Set creation completed.";
                });
            }
            catch (Exception e) when (e is InvalidDataException || e is FileNotFoundException)
            {
                RxSchedulers.MainThreadScheduler.Schedule(() => {
                   ActionProgressLabel = "Tile Set Build Failed: Internal error! " + e.Message;
                });
            }
            catch (Exception e)
            {
                RxSchedulers.MainThreadScheduler.Schedule(() => {
                    ActionProgressLabel = "Tile Set Build Failed: Internal error! " + e.Message;
                });
            }
        });
    }
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
public class VirtualTextureGridItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _format = string.Empty;
    private string _dimensions = string.Empty;
    private string _status = "Pending";
    public string Name { get => _name; set { _name = value; OnPropertyChanged(nameof(Name)); } }
    public string Format { get => _format; set { _format = value; OnPropertyChanged(nameof(Format)); } }
    public string Dimensions { get => _dimensions; set { _dimensions = value; OnPropertyChanged(nameof(Dimensions)); } }
    public string Status { get => _status; set { _status = value; OnPropertyChanged(nameof(Status)); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}