using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading; // Required for safe UI thread updates
using LSLib.LS;
using LSLib.LS.Enums;
using ReactiveUI;
using ReactiveUI.Avalonia;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel; // Required for ObservableCollection
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;

namespace LSTools.DivineGUI;

public class LocalizationEntryItem
{
    public string Key { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public partial class LocalizationPane : ReactiveUserControl<LocalizationPane>, IGameSettingsTarget, INotifyPropertyChanged
{
    private Game _targetGameVersion = Game.BaldursGate3;

    public ObservableCollection<LocalizationEntryItem> LocalizationEntries { get; } = [];

    public string LocaInputPath { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(LocaInputPath)); } } } = string.Empty;
    public string LocaOutputPath { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(LocaOutputPath)); } } } = string.Empty;

    public string ProgressText { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(ProgressText)); } } } = string.Empty;

    public ReactiveCommand<Unit, Unit> BrowseInputLocationCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseOutputLocationCommand { get; }
    public ReactiveCommand<Unit, Unit> ConvertLocalizationCommand { get; }

    private static readonly FilePickerFileType LocaFileTypes = new("Localization files")
    {
        Patterns = ["*.loca", "*.xml"]
    };

    public LocalizationPane()
    {
        InitializeComponent();
        DataContext = this;
        ViewModel = this;

        BrowseInputLocationCommand = ReactiveCommand.CreateFromTask(BrowseInputLocationAsync);
        BrowseOutputLocationCommand = ReactiveCommand.CreateFromTask(BrowseOutputLocationAsync);
        ConvertLocalizationCommand = ReactiveCommand.CreateFromTask(ExecuteLocalizationConversionAsync);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public void SetGame(Game game)
    {
        _targetGameVersion = game;
    }

    private async Task BrowseInputLocationAsync()
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null) return;

        var files = await provider.OpenFilePickerAsync(new()
        {
            Title = "Select Input File",
            AllowMultiple = false,
            FileTypeFilter = [LocaFileTypes]
        });

        if (files.Count > 0) LocaInputPath = files[0].Path.LocalPath;
    }

    private async Task BrowseOutputLocationAsync()
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null) return;

        var file = await provider.SaveFilePickerAsync(new()
        {
            Title = "Select Output File",
            FileTypeChoices = [LocaFileTypes],
            DefaultExtension = "loca"
        });

        if (file is not null) LocaOutputPath = file.Path.LocalPath;
    }

    private async Task ExecuteLocalizationConversionAsync()
    {
        if (string.IsNullOrWhiteSpace(LocaInputPath) || !File.Exists(LocaInputPath)) return;
        if (string.IsNullOrWhiteSpace(LocaOutputPath)) return;

        ProgressText = "Parsing and translating localization parameters...";
        Dispatcher.UIThread.Post(() => LocalizationEntries.Clear());

        try
        {
            string input = LocaInputPath;
            string output = LocaOutputPath;

            await Task.Run(() =>
            {
                var resource = LocaUtils.Load(input);
                var format = LocaUtils.ExtensionToFileFormat(output);
                LocaUtils.Save(resource, output, format);

                if (resource != null)
                {
                    var parsedGridItems = new List<LocalizationEntryItem>();

                    foreach (var entry in resource.Entries)
                    {
                        parsedGridItems.Add(new LocalizationEntryItem
                        {
                            Key = entry.Key ?? string.Empty,
                            Version = entry.Version.ToString(),
                            Value = entry.Text ?? string.Empty
                        });
                    }
                    Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var item in parsedGridItems)
                        {
                            LocalizationEntries.Add(item);
                        }
                    });
                }
            });

            ProgressText = "Localization file converted and saved successfully.";
        }
        catch (Exception exc)
        {
            ProgressText = $"Conversion Failed!\n\nInternal error:\n{exc.Message}";
        }
    }

    private PropertyChangedEventHandler? _localPropertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => _localPropertyChanged += value;
        remove => _localPropertyChanged -= value;
    }

    private void InvokeLocalProperty(string propertyName)
    {
        _localPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
