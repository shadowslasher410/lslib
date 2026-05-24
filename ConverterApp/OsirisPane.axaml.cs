using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LSLib.LS.Story;
using ReactiveUI;
using ReactiveUI.Avalonia;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive;   
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;

namespace LSTools.DivineGUI;

public class DatabaseItemModel
{
    public int Index { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public class FactRowModel(Fact factInstance, string[] columnDisplayValues)
{
    public Fact FactInstance { get; } = factInstance;
    public string[] ColumnDisplayValues { get; } = columnDisplayValues;

    public string ArgumentsValue => ColumnDisplayValues != null ? string.Join(", ", ColumnDisplayValues) : string.Empty;
}


public partial class OsirisPane : ReactiveUserControl<OsirisPane>, INotifyPropertyChanged
{
    private Story? _story;
    private List<DatabaseItemModel> _allDatabaseItems = [];

    public string StoryFilePath { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(StoryFilePath)); } } } = string.Empty;
    public string GoalOutputPath { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(GoalOutputPath)); } } } = string.Empty;
    public string FilterText { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(FilterText)); FilterDatabaseDropdownListModern(); } } } = string.Empty;
    public bool MatchCaseFilter { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(MatchCaseFilter)); FilterDatabaseDropdownListModern(); } } }
    public bool IsFilterValid { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(IsFilterValid)); } } } = true;
    public bool IsDecompileProcessing { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(IsDecompileProcessing)); } } }
    public string ProgressStatusText { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(ProgressStatusText)); } } } = string.Empty;
    public string ErrorMessage { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(ErrorMessage)); } } } = string.Empty;
    public string SuccessMessage { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(SuccessMessage)); } } } = string.Empty;

    public DatabaseItemModel? SelectedDatabase
    {
        get;
        set { if (field != value) { field = value; InvokeLocalProperty(nameof(SelectedDatabase)); RefreshDataGrid(); } }
    }

    public ObservableCollection<FactRowModel> GridFactRows { get; } = [];
    public ObservableCollection<DatabaseItemModel> FilteredDatabaseItems { get; } = [];

    public ICommand BrowseStoryFileCommand { get; }
    public ICommand BrowseGoalFolderCommand { get; }
    public ICommand LoadStoryCommand { get; }
    public ICommand SaveStoryCommand { get; }
    public ICommand DecompileStoryCommand { get; }
    public ICommand ExportDebugJsonCommand { get; }
    public ICommand ApplyFilterCommand { get; }

    private static readonly FilePickerFileType StoryFileTypes = new("LS story / savegame files")
    {
        Patterns = ["*.osi", "*.lsv"]
    };

    public OsirisPane()
    {
        InitializeComponent();
        DataContext = this;
        ViewModel = this;

        BrowseStoryFileCommand = ReactiveCommand.CreateFromTask(BrowseStoryFileAsync);
        BrowseGoalFolderCommand = ReactiveCommand.CreateFromTask(BrowseGoalFolderAsync);

        LoadStoryCommand = ReactiveCommand.CreateFromTask(ExecuteLoadStoryAsync);
        SaveStoryCommand = ReactiveCommand.CreateFromTask(ExecuteSaveStoryAsync);
        ApplyFilterCommand = ReactiveCommand.Create(FilterDatabaseDropdownListModern);
        var propertyChangedObservable = Observable.FromEventPattern<PropertyChangedEventHandler, PropertyChangedEventArgs>(
           h => _localPropertyChanged += h,
           h => _localPropertyChanged -= h);

        var canDecompile = propertyChangedObservable
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default)
            .Select(_ => _story != null && !string.IsNullOrWhiteSpace(GoalOutputPath) && Directory.Exists(GoalOutputPath))
            .DistinctUntilChanged();
        DecompileStoryCommand = ReactiveCommand.CreateFromTask(ExecuteDecompileStoryAsync, canDecompile);
        ExportDebugJsonCommand = ReactiveCommand.CreateFromTask(ExecuteExportDebugJsonAsync, canDecompile);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async Task BrowseStoryFileAsync()
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null) return;

        var files = await provider.OpenFilePickerAsync(new() { Title = "Select Story/Savegame File", AllowMultiple = false, FileTypeFilter = [StoryFileTypes] });
        if (files.Count > 0) StoryFilePath = files[0].Path.LocalPath;
    }

    private async Task BrowseGoalFolderAsync()
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null) return;

        var folders = await provider.OpenFolderPickerAsync(new() { Title = "Select Goal Destination Folder", AllowMultiple = false });
        if (folders.Count > 0) GoalOutputPath = folders[0].Path.LocalPath;
    }

    private async Task ExecuteLoadStoryAsync()
    {
        if (string.IsNullOrWhiteSpace(StoryFilePath) || !File.Exists(StoryFilePath)) return;
        ProgressStatusText = "Loading story binaries into memory cache...";
        IsDecompileProcessing = true;

        try
        {
            await Task.Run(() =>
            {
                using var stream = File.OpenRead(StoryFilePath);
                var story = StoryReader.Read(stream);

                var items = new List<DatabaseItemModel>();
                foreach (var db in story.Databases)
                {
                    items.Add(new DatabaseItemModel
                    {
                        Index = (int)db.Key - 1,
                        DisplayName = $"Database {db.Key} (Parameters: {db.Value.Parameters.Types.Count})"
                    });
                }

                RxSchedulers.MainThreadScheduler.Schedule(Unit.Default, (_, _) =>
                {
                    SetLoadedStory(story, items);
                    return Disposable.Empty;
                });
            });
            SuccessMessage = "Story payload successfully processed.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsDecompileProcessing = false; }
    }

    private async Task ExecuteSaveStoryAsync()
    {
        if (_story == null || string.IsNullOrWhiteSpace(StoryFilePath)) return;
        ProgressStatusText = "Writing database compilation parameters back to disk...";
        IsDecompileProcessing = true;

        try
        {
            await Task.Run(() =>
            {
                using var stream = File.Open(StoryFilePath, FileMode.Create, FileAccess.Write);
                StoryWriter.Write(stream, _story, false);
            });
            SuccessMessage = "Database parameters saved successfully.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsDecompileProcessing = false; }
    }

    public void SetLoadedStory(Story story, List<DatabaseItemModel> items)
    {
        _story = story;
        _allDatabaseItems = items;
        FilterDatabaseDropdownListModern();
        InvokeLocalProperty(nameof(_story));
    }

    private void FilterDatabaseDropdownListModern()
    {
        FilteredDatabaseItems.Clear();
        string filter = FilterText?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(filter))
        {
            IsFilterValid = true;
            foreach (var item in _allDatabaseItems) FilteredDatabaseItems.Add(item);
            SelectedDatabase = FilteredDatabaseItems.FirstOrDefault();
            return;
        }

        var comparison = MatchCaseFilter ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var results = _allDatabaseItems.Where(x => x.DisplayName.Contains(filter, comparison)).ToList();

        IsFilterValid = results.Count > 0;
        foreach (var item in results) FilteredDatabaseItems.Add(item);
        SelectedDatabase = FilteredDatabaseItems.FirstOrDefault();
    }

    private void RefreshDataGrid()
    {
        GridFactRows.Clear();
        var grid = this.FindControl<DataGrid>("databaseGrid");
        if (grid == null || _story == null || SelectedDatabase == null) return;

        grid.Columns.Clear();
        uint targetDbKey = (uint)(SelectedDatabase.Index + 1);
        if (!_story.Databases.TryGetValue(targetDbKey, out var database)) return;

        int totalParameters = database.Parameters.Types.Count;
        for (int i = 0; i < totalParameters; i++)
        {
            int colIndex = i;
            string typeName = _story.Types[database.Parameters.Types[i]].Name;

            var programmaticCellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<FactRowModel>(
                (row, _) =>
                {
                    var textBlock = new TextBlock
                    {
                        Margin = new Avalonia.Thickness(12, 6),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    };
                    if (row != null && row.ColumnDisplayValues != null && colIndex < row.ColumnDisplayValues.Length)
                    {
                        textBlock.Text = row.ColumnDisplayValues[colIndex];
                    }

                    return textBlock;
                },
                supportsRecycling: true 
            );

            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = $"{colIndex} ({typeName})",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                CellTemplate = programmaticCellTemplate
            });
        }

        if (database.Facts != null)
        {
            foreach (var fact in database.Facts)
            {
                var values = new string[totalParameters];
                for (int i = 0; i < totalParameters; i++)
                {
                    using var writer = new StringWriter();
                    fact.Columns[i].DebugDump(writer, _story);
                    values[i] = writer.ToString();
                }
                GridFactRows.Add(new FactRowModel(fact, values));
            }
        }
    }

    private async Task ExecuteDecompileStoryAsync()
    {
        if (_story == null) return;
        ErrorMessage = string.Empty; SuccessMessage = string.Empty; IsDecompileProcessing = true;
        ProgressStatusText = "Decompiling story nodes and dumping goals...";
        string targetDir = GoalOutputPath;
        try
        {
            await Task.Run(() =>
            {
                string debugPath = Path.Combine(targetDir, "debug.log");
                using (var debugFile = new FileStream(debugPath, FileMode.Create, FileAccess.Write))
                using (var writer = new StreamWriter(debugFile)) { _story.DebugDump(writer); }
                string unassignedPath = Path.Combine(targetDir, "UNASSIGNED_RULES.txt");
                using (var goalFile = new FileStream(unassignedPath, FileMode.Create, FileAccess.Write))
                using (var writer = new StreamWriter(goalFile))
                {
                    var dummyGoal = new Goal(_story) { ExitCalls = [], InitCalls = [], ParentGoals = [], SubGoals = [], Name = "UNASSIGNED_RULES", Index = 0 };
                    dummyGoal.MakeScript(writer, _story);
                }
                foreach (var goalEntry in _story.Goals)
                {
                    var goal = goalEntry.Value;
                    string filePath = Path.Combine(targetDir, "{goal.Name}.txt"); using var goalFile = new FileStream(filePath, FileMode.Create, FileAccess.Write); using var writer = new StreamWriter(goalFile); goal.MakeScript(writer, _story);
                }
            }); SuccessMessage = "Story unpacked and decompiled successfully.";
        }
        catch (Exception ex) { ErrorMessage = $"Decompilation Failed: {ex.Message}"; }
        finally { IsDecompileProcessing = false; ProgressStatusText = string.Empty; }
    }
    private async Task ExecuteExportDebugJsonAsync()
    {
        if (_story == null) return;
        ErrorMessage = string.Empty; SuccessMessage = string.Empty;
        string filePath = Path.Combine(GoalOutputPath, "debug.json");
        try
        {
            await Task.Run(() =>
            {
                using var debugFileStream = new FileStream(filePath, FileMode.Create);
                var sev = new StoryDebugExportVisitor(debugFileStream);
                sev.Visit(_story);
            });
            SuccessMessage = "Debug metadata JSON successfully exported.";
        }
        catch (Exception ex) { ErrorMessage = $"JSON Export Failed: {ex.Message}"; }
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