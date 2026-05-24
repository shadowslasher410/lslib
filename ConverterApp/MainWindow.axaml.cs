using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LSLib.LS;
using LSLib.LS.Enums;
using ReactiveUI;
using ReactiveUI.Avalonia;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LSTools.DivineGUI;

public partial class MainWindow : ReactiveWindow<MainWindow>, INotifyPropertyChanged
{
    public ConverterAppSettings Settings
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                InvokeLocalProperty(nameof(Settings));
            }
        }
    } = null!;

    public string AppTitle
    {
        get;
        set { if (field != value) { field = value; InvokeLocalProperty(nameof(AppTitle)); } }
    } = "LSLib Toolkit";

    public string GlobalAlertLog
    {
        get;
        set { if (field != value) { field = value; InvokeLocalProperty(nameof(GlobalAlertLog)); } }
    } = string.Empty;

    public string[] DynamicGameOptions { get; } =
    [
        "Divinity: Original Sin (32-bit)",
        "Divinity: Original Sin EE (64-bit)",
        "Divinity: Original Sin 2 (64-bit)",
        "Divinity: Original Sin 2 DE (64-bit)",
        "Baldur's Gate 3 (64-bit)"
    ];

    public ReactiveCommand<int, Unit> GameSelectionChangedCommand { get; }

    public MainWindow()
    {
        InitializeComponent();

        GameSelectionChangedCommand = ReactiveCommand.Create<int>(targetIndex =>
        {
            if (Settings == null) return;
            Settings.SelectedGame = targetIndex;

            Game mappedGame = (Game)targetIndex;

            var gr2Control = this.Find<GR2Pane>("gr2PaneControl");
            var packageControl = this.Find<PackagePane>("packagePaneControl");
            var osirisControl = this.Find<OsirisPane>("osirisPaneControl");
            var debugControl = this.Find<DebugPane>("debugPaneControl");

            if (gr2Control is IGameSettingsTarget gr2Target) gr2Target.SetGame(mappedGame);
            if (packageControl is IGameSettingsTarget packageTarget) packageTarget.SetGame(mappedGame);
            if (osirisControl is IGameSettingsTarget osirisTarget) osirisTarget.SetGame(mappedGame);
            if (debugControl is IGameSettingsTarget debugTarget) debugTarget.SetGame(mappedGame);

            gr2Control?.FlipMeshes = mappedGame.IsFW3(); 
        });
    }

    public MainWindow(ISettingsDataSource settingsDataSource) : this()
    {
        ArgumentNullException.ThrowIfNull(settingsDataSource);
        Settings = settingsDataSource.Settings;
        ViewModel = this;

        LoadSettingsProfile();

        AppTitle = $"LSLib Toolkit (LSLib v{Common.LibraryVersion()})";
        Settings.Version = Common.LibraryVersion();

        Settings.SetPropertyChangedEvent(OnSettingsProfileMutated);

        Settings.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(ConverterAppSettings.SelectedGame))
            {
                RxSchedulers.MainThreadScheduler.Schedule(() =>
                {
                    GameSelectionChangedCommand.Execute(Settings.SelectedGame).Subscribe();
                });
            }
        };

        GameSelectionChangedCommand.Execute(Settings.SelectedGame).Subscribe();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void LoadSettingsProfile()
    {
        try
        {
            string profilePath = Path.Combine(AppContext.BaseDirectory, "settings.json");
            if (File.Exists(profilePath))
            {
                byte[] jsonBytes = File.ReadAllBytes(profilePath);

                var context = new AppSettingsJsonContext();
                var resolvedProfile = JsonSerializer.Deserialize(jsonBytes, context.ConverterAppSettings);
                if (resolvedProfile != null) Settings = resolvedProfile;
            }
        }
        catch (Exception ex)
        {
            GlobalAlertLog = $"Failed to restore settings configuration: {ex.Message}";
        }
    }

    private void OnSettingsProfileMutated(object? sender, PropertyChangedEventArgs e)
    {
        Task.Run(() =>
        {
            try
            {
                string profilePath = Path.Combine(AppContext.BaseDirectory, "settings.json");
                var context = new AppSettingsJsonContext();

                byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(Settings, context.ConverterAppSettings);
                File.WriteAllBytes(profilePath, jsonBytes);
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    GlobalAlertLog = $"Failed to serialize configuration properties to file: {ex.Message}";
                });
            }
        });
    }

    private PropertyChangedEventHandler? _localPropertyChanged;

    public new event PropertyChangedEventHandler? PropertyChanged
    {
        add => _localPropertyChanged += value;
        remove => _localPropertyChanged -= value;
    }

    private void InvokeLocalProperty(string propertyName)
    {
        _localPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

[JsonSerializable(typeof(ConverterAppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext;