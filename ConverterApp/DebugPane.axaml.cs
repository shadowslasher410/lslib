using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LSLib.LS.Enums;
using LSLib.LS.Story.Compiler;
using ReactiveUI.Avalonia;
using System.Collections.ObjectModel; 
using System.ComponentModel;
using System.Windows.Input;

namespace LSTools.DivineGUI;

public partial class DebugPane : ReactiveUserControl<DebugPane>, IGameSettingsTarget, INotifyPropertyChanged
{
    private readonly ISettingsDataSource? _settingsDataSource;

    public ObservableCollection<Diagnostic> DiagnosticLogs { get; } = [];

    public Game Game { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(Game)); } } } = Game.BaldursGate3;

    public string SaveFilePath
    {
        get => _settingsDataSource?.Settings?.Debugging?.SavePath ?? string.Empty;
        set
        {
            if (_settingsDataSource?.Settings?.Debugging != null && _settingsDataSource.Settings.Debugging.SavePath != value)
            {
                _settingsDataSource.Settings.Debugging.SavePath = value;
                InvokeLocalProperty(nameof(SaveFilePath));
                UpdateCommandStates();
            }
        }
    }

    public bool ExtractAll { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(ExtractAll)); } } } = true;
    public bool ConvertToLsx { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(ConvertToLsx)); } } } = true;
    public bool DumpModList { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(DumpModList)); } } } = true;
    public bool DumpGlobalVars { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(DumpGlobalVars)); } } } = true;
    public bool DumpCharacterVars { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(DumpCharacterVars)); } } } = true;
    public bool DumpItemVars { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(DumpItemVars)); } } } = true;
    public bool IncludeDeletedVars { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(IncludeDeletedVars)); } } }
    public bool IncludeLocalScopes { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(IncludeLocalScopes)); } } }
    public bool DumpStoryDatabases { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(DumpStoryDatabases)); } } } = true;
    public bool IncludeUnnamedDatabases { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(IncludeUnnamedDatabases)); } } }
    public int ProgressPercentage { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(ProgressPercentage)); } } }
    public string ProgressStatusText { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(ProgressStatusText)); } } } = string.Empty;
    public string ErrorMessage { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(ErrorMessage)); } } } = string.Empty;
    public string SuccessMessage { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(SuccessMessage)); } } } = string.Empty;

    public ICommand SaveFileBrowseCommand { get; }
    public ICommand DumpVariablesCommand { get; }

    public DebugPane()
    {
        InitializeComponent();
        DataContext = this;
        ViewModel = this;

        SaveFileBrowseCommand = new DebugPaneRelayCommand(_ => _ = ExecuteBrowseAsync());
        DumpVariablesCommand = new DebugPaneRelayCommand(_ => _ = ExecuteDumpAsync(), () => !string.IsNullOrWhiteSpace(SaveFilePath) && SaveFilePath.EndsWith(".lsv", StringComparison.OrdinalIgnoreCase));
    }

    public DebugPane(ISettingsDataSource settingsDataSource) : this()
    {
        _settingsDataSource = settingsDataSource ?? throw new ArgumentNullException(nameof(settingsDataSource));
    }


    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void UpdateCommandStates() => ((DebugPaneRelayCommand)DumpVariablesCommand).RaiseCanExecuteChanged();

    public void SetGame(Game game)
    {
        Game = game;
    }

    private DebugDumperTask CreateDumperFromSettings()
    {
        string directoryName = Path.GetDirectoryName(SaveFilePath) ?? AppContext.BaseDirectory;
        string fileName = Path.GetFileNameWithoutExtension(SaveFilePath);
        string dumpPath = Path.Combine(directoryName, fileName);

        return new DebugDumperTask
        {
            GameVersion = Game,
            ExtractionPath = Path.Combine(dumpPath, "SaveArchive"),
            DataDumpPath = Path.Combine(dumpPath, "Dumps"),
            SaveFilePath = SaveFilePath,
            ExtractAll = ExtractAll,
            ConvertToLsx = ConvertToLsx,
            DumpModList = DumpModList,
            DumpGlobalVars = DumpGlobalVars,
            DumpCharacterVars = DumpCharacterVars,
            DumpItemVars = DumpItemVars,
            IncludeDeletedVars = IncludeDeletedVars,
            IncludeLocalScopes = IncludeLocalScopes,
            DumpStoryDatabases = DumpStoryDatabases,
            IncludeUnnamedDatabases = IncludeUnnamedDatabases
        };
    }

    private async Task ExecuteBrowseAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open LSLib Savegame Package File",
            AllowMultiple = false,
            FileTypeFilter = [
                new FilePickerFileType("Larian Savegame Package (*.lsv)")
                {
                    Patterns = ["*.lsv"]
                }
            ]
        });

        if (files.Count > 0)
        {
            SaveFilePath = files[0].Path.LocalPath;
        }
    }

    private async Task ExecuteDumpAsync()
    {
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;
        DiagnosticLogs.Clear(); // Clear out previous run diagnostics

        var dumper = CreateDumperFromSettings();

        dumper.ProgressUpdated += (args) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                ProgressPercentage = args.Percentage;
                ProgressStatusText = args.StatusText;
            });
        };

        try
        {
            await dumper.RunAsync();
            SuccessMessage = $"Savegame successfully dumped to:\n{dumper.DataDumpPath}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Dump Failed!\n\nInternal processing error:\n{ex.Message}";
        }
        finally
        {
            // FIX: Copies the processed background logs out of your task onto the UI thread grid collection
            foreach (var log in dumper.TaskDiagnostics)
            {
                DiagnosticLogs.Add(log);
            }

            ProgressPercentage = 0;
            ProgressStatusText = string.Empty;
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

public class DebugPaneRelayCommand(Action<object?> execute, Func<bool>? canExecute = null) : ICommand
{
    private readonly Action<object?> _execute = execute;
    private readonly Func<bool>? _canExecute = canExecute;
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();
    public void Execute(object? parameter) => _execute(parameter);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}