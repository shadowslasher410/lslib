using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LSLib.Granny;
using LSLib.Granny.GR2;
using LSLib.Granny.Model;
using LSLib.LS.Enums;
using ReactiveUI.Avalonia;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace LSTools.DivineGUI;

public class ExportableObjectModel : INotifyPropertyChanged
{
    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set { if (_isChecked != value) { _isChecked = value; OnPropertyChanged(nameof(IsChecked)); } }
    }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ResourceFormatModel : INotifyPropertyChanged
{
    private string _selectedFormat = "Automatic";
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string SelectedFormat
    {
        get => _selectedFormat;
        set { if (_selectedFormat != value) { _selectedFormat = value; OnPropertyChanged(nameof(SelectedFormat)); } }
    }
    public string[] AvailableFormats { get; set; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class GR2Pane : ReactiveUserControl<GR2Pane>, IGameSettingsTarget, INotifyPropertyChanged
{
    private readonly ISettingsDataSource? _settingsDataSource;
    private Root? _root;
    private ExporterOptions? _lastExporterSettings;

#pragma warning disable CS0067
    public new event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067

    public ObservableCollection<ExportableObjectModel> ExportableObjects { get; } = [];
    public ObservableCollection<ResourceFormatModel> ResourceFormats { get; } = [];

    public bool FlipUVs { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(FlipUVs)); } } }
    public bool FilterUVs { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(FilterUVs)); } } }
    public bool ApplyBasisTransforms { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(ApplyBasisTransforms)); } } }
    public bool FlipMeshes { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(FlipMeshes)); } } }
    public bool MirrorSkeletons { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(MirrorSkeletons)); } } }

    public bool ConformToOriginalEnabled { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(ConformToOriginalEnabled)); } } }
    public string ConformToOriginalText { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(ConformToOriginalText)); } } } = "Conform to original GR2:";
    public bool ConformToOriginalChecked { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(ConformToOriginalChecked)); ConformCopySkeletonsEnabled = value; } } }
    public bool BuildDummySkeletonEnabled { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(BuildDummySkeletonEnabled)); } } }
    public bool BuildDummySkeletonChecked { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(BuildDummySkeletonChecked)); } } }
    public bool ConformCopySkeletonsEnabled { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(ConformCopySkeletonsEnabled)); } } }
    public bool SaveOutputBtnEnabled { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(SaveOutputBtnEnabled)); UpdateCommandStates(); } } }

    public bool MeshRigidEnabled { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(MeshRigidEnabled)); } } }
    public bool MeshClothEnabled { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(MeshClothEnabled)); } } }
    public bool MeshProxyEnabled { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(MeshProxyEnabled)); } } }
    public bool MeshRigidChecked { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(MeshRigidChecked)); } } }
    public bool MeshClothChecked { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(MeshClothChecked)); } } }
    public bool MeshProxyChecked { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(MeshProxyChecked)); } } }

    public string ProgressStatusText { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(ProgressStatusText)); } } } = string.Empty;
    public string BatchStatusText { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(BatchStatusText)); } } } = string.Empty;
    public int BatchProgressValue { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(BatchProgressValue)); } } }
    public string ErrorMessage { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(ErrorMessage)); } } } = string.Empty;
    public string SuccessMessage { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(SuccessMessage)); } } } = string.Empty;

    public ExportableObjectModel? SelectedObjectRow { get; set { if (field != value) { field = value; InvokeLocalProperty(nameof(SelectedObjectRow)); } } }

    public string[] InputFormats { get; } = ["GR2", "DAE", "GLTF", "GLB"];
    public string[] OutputFormats { get; } = ["DAE", "GR2", "GLTF", "GLB"];

    public ConverterAppSettings? Settings => _settingsDataSource?.Settings;

    public ICommand BrowsePathCommand { get; }
    public ICommand LoadInputCommand { get; }
    public ICommand SaveOutputCommand { get; }
    public ICommand BatchConvertCommand { get; }

    public GR2Pane()
    {
        InitializeComponent();
        DataContext = this;
        ViewModel = this;

        BrowsePathCommand = new PaneToolkitRelayCommand(new Action<object?>(_ => _ = ExecuteBrowseAsync((string)(_ ?? string.Empty))));

        // FIXED: Using fully qualified System.Func<bool> namespace tokens completely avoids ambiguities with game database 'Func' classes
        LoadInputCommand = new PaneToolkitRelayCommand(
            new Action<object?>(_ => _ = ExecuteLoadInputAsync()),
            new Func<bool>(() => Settings?.GR2 != null && !string.IsNullOrWhiteSpace(Settings.GR2.InputPath)));

        SaveOutputCommand = new PaneToolkitRelayCommand(
            new Action<object?>(_ => _ = ExecuteSaveOutputAsync()),
            new Func<bool>(() => SaveOutputBtnEnabled));

        BatchConvertCommand = new PaneToolkitRelayCommand(
            new Action<object?>(_ => _ = ExecuteBatchConvertAsync()),
            new Func<bool>(() => Settings?.GR2 != null && !string.IsNullOrWhiteSpace(Settings.GR2.BatchInputPath) && !string.IsNullOrWhiteSpace(Settings.GR2.BatchOutputPath)));
    }

    public GR2Pane(ISettingsDataSource settingsDataSource) : this()
    {
        _settingsDataSource = settingsDataSource;
        if (Settings?.GR2 != null)
        {
            Settings.GR2.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GR2PaneSettings.InputPath)) ((PaneToolkitRelayCommand)LoadInputCommand).RaiseCanExecuteChanged();
                if (e.PropertyName is nameof(GR2PaneSettings.BatchInputPath) or nameof(GR2PaneSettings.BatchOutputPath))
                    ((PaneToolkitRelayCommand)BatchConvertCommand).RaiseCanExecuteChanged();
            };
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    private void UpdateCommandStates() => ((PaneToolkitRelayCommand)SaveOutputCommand).RaiseCanExecuteChanged();

    public void SetGame(Game game)
    {
        Settings?.SelectedGame = (int)game;
    }

    private async Task ExecuteBrowseAsync(string targetKey)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        if (targetKey.Contains("Batch"))
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = $"Select {targetKey}" });
            if (folders.Count > 0) UpdateSettingPath(targetKey, folders[0].Path.LocalPath);
        }
        else
        {
            bool isConform = targetKey is "ConformPath";
            var fileTypeFilter = isConform
                ? new FilePickerFileType("Granny Skeleton Files (*.gr2, *.lsm)") { Patterns = ["*.gr2", "*.lsm"] }
                : new FilePickerFileType("3D Assets (*.dae, *.gr2, *.lsm, *.gltf, *.glb)") { Patterns = ["*.dae", "*.gr2", "*.lsm", "*.gltf", "*.glb"] };

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = $"Select {targetKey}", AllowMultiple = false, FileTypeFilter = [fileTypeFilter] });
            if (files.Count > 0) UpdateSettingPath(targetKey, files[0].Path.LocalPath);
        }
    }

    private void UpdateSettingPath(string key, string path)
    {
        if (Settings?.GR2 == null) return;
        switch (key)
        {
            case "InputPath": Settings.GR2.InputPath = path; break;
            case "OutputPath": Settings.GR2.OutputPath = path; break;
            case "ConformPath": Settings.GR2.ConformPath = path; break;
            case "BatchInputPath": Settings.GR2.BatchInputPath = path; break;
            case "BatchOutputPath": Settings.GR2.BatchOutputPath = path; break;
        }
        InvokeLocalProperty(nameof(Settings));
        ((PaneToolkitRelayCommand)LoadInputCommand).RaiseCanExecuteChanged();
        ((PaneToolkitRelayCommand)BatchConvertCommand).RaiseCanExecuteChanged();
    }
    private async Task ExecuteLoadInputAsync()
    {
        if (Settings?.GR2 == null) return;
        ErrorMessage = string.Empty; SuccessMessage = string.Empty;
        ProgressStatusText = "Loading model file structure...";
        try
        {
            string path = Settings.GR2.InputPath;
            var loadedRoot = await Task.Run(() => GR2Utils.LoadModel(path));
            Dispatcher.UIThread.Post(() =>
            {
                _root = loadedRoot;
                UpdateInputState();
                SuccessMessage = "Asset structure successfully loaded.";
            });
        }
        catch (Exception exc)
        {
            ErrorMessage = $"Import failed: {exc.Message}";
            SaveOutputBtnEnabled = false;
        }
        finally { ProgressStatusText = string.Empty; }
    }
    private async Task ExecuteSaveOutputAsync()
    {
        ErrorMessage = string.Empty; SuccessMessage = string.Empty;
        ProgressStatusText = "Running exporter operations...";
        var exporter = new Exporter();
        try
        {
            UpdateExporterSettings(exporter.Options);
            _lastExporterSettings = exporter.Options;
            await Task.Run(() => exporter.Export());
            SuccessMessage = "Export completed successfully.";
        }
        catch (Exception exc) { ProcessConversionError(exporter.Options.InputPath, exporter.Options.OutputPath, exc); }
        finally { ProgressStatusText = string.Empty; }
    }
    private async Task ExecuteBatchConvertAsync()
    {
        if (Settings?.GR2 == null) return;
        ErrorMessage = string.Empty; SuccessMessage = string.Empty;
        var exporter = new Exporter();
        UpdateCommonExporterSettings(exporter.Options);
        exporter.Options.InputFormat = (ExportFormat)Settings.GR2.BatchInputFormat;
        exporter.Options.OutputFormat = (ExportFormat)Settings.GR2.BatchOutputFormat;
        var batchConverter = new GR2Utils();
        batchConverter.ProgressUpdate += (status, num, den) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                BatchStatusText = status;
                BatchProgressValue = den == 0 ? 0 : (int)(num * 100 / den);
            });
        };
        batchConverter.ConversionError += (inPath, outPath, exc) =>
        {
            Dispatcher.UIThread.Post(() => ProcessConversionError(inPath, outPath, exc));
        };
        try
        {
            string inDir = Settings.GR2.BatchInputPath;
            string outDir = Settings.GR2.BatchOutputPath;
            await Task.Run(() => batchConverter.ConvertModels(inDir, outDir, exporter));
            SuccessMessage = "Batch asset export finished successfully.";
        }
        catch (Exception ex) { ErrorMessage = $"Batch pipeline interrupted: {ex.Message}"; }
        finally
        {
            BatchStatusText = string.Empty;
            BatchProgressValue = 0;
        }
    }
    private void ProcessConversionError(string inputPath, string outputPath, Exception exc)
    {
        string pathText = $"Input: {inputPath}\nOutput: {outputPath}\n\n"; ErrorMessage = exc is ExportException or ParsingException ? $"Export script aborted!\n{pathText}{exc.Message}"
        : $"Internal critical conversion fault:\n{pathText}{exc}";
    }
    private void UpdateExportableObjects()
    {
        ExportableObjects.Clear();
        if (_root == null) return;
        if (_root.Models != null)
            foreach (var model in _root.Models) ExportableObjects.Add(new() { Name = model.Name, Type = "Model", IsChecked = true });
        if (_root.Skeletons != null)
            foreach (var skeleton in _root.Skeletons) ExportableObjects.Add(new() { Name = skeleton.Name, Type = "Skeleton", IsChecked = true });
        if (_root.Animations != null)
            foreach (var animation in _root.Animations) ExportableObjects.Add(new() { Name = animation.Name, Type = "Animation", IsChecked = true });
    }
    private void UpdateResourceFormats()
    {
        ResourceFormats.Clear();
        if (_root == null) return;
        string[] meshFormats = ["Automatic", "GR2", "DAE", "OBJ", "FBX", "GLTF"];
        string[] dynamicCurveTypes = ["Automatic", "Constant32BitKeyframe", "Constant64BitKeyframe", "Linear32BitKeyframe", "Linear64BitKeyframe", "CubicBezier32BitKeyframe", "CubicBezier64BitKeyframe"];
        if (_root.Meshes != null)
        {
            foreach (var obj in _root.Meshes)
            {
                if (obj is Mesh mesh)
                {
                    ResourceFormats.Add(new ResourceFormatModel { Name = mesh.Name, Type = "Mesh", AvailableFormats = meshFormats });
                }
            }
        }
        if (_root.TrackGroups != null)
        {
            foreach (var trackGroup in _root.TrackGroups)
            {
                if (trackGroup.TransformTracks == null) continue;
                foreach (var track in trackGroup.TransformTracks)
                {
                    if (track.PositionCurve != null)
                        ResourceFormats.Add(new ResourceFormatModel { Name = track.Name, Type = "Position Track", AvailableFormats = dynamicCurveTypes });
                    if (track.OrientationCurve != null)
                        ResourceFormats.Add(new ResourceFormatModel { Name = track.Name, Type = "Rotation Track", AvailableFormats = dynamicCurveTypes });
                    if (track.ScaleShearCurve != null)
                        ResourceFormats.Add(new ResourceFormatModel { Name = track.Name, Type = "Scale/Shear Track", AvailableFormats = dynamicCurveTypes });
                }
            }
        }
    }
    private void UpdateInputState()
    {
        if (_root == null) return;
        bool skinned = _root.Skeletons != null && _root.Skeletons.Count > 0;
        bool animationsOnly = !skinned && (_root.Models == null || _root.Models.Count == 0) && (_root.Animations != null && _root.Animations.Count > 0);
        if (skinned)
        {
            ConformToOriginalEnabled = true;
            ConformToOriginalText = "Conform to original GR2:";
            BuildDummySkeletonEnabled = false;
            BuildDummySkeletonChecked = false;
        }
        else if (animationsOnly)
        {
            ConformToOriginalEnabled = true;
            ConformToOriginalText = "Copy skeleton from:";
            BuildDummySkeletonEnabled = false;
            BuildDummySkeletonChecked = false;
        }
        else
        {
            ConformToOriginalEnabled = false;
            ConformToOriginalChecked = false;
            ConformCopySkeletonsEnabled = false;
            BuildDummySkeletonEnabled = true;
            BuildDummySkeletonChecked = true;
        }
        bool hasUndeterminedModelTypes = false;
        DivinityModelFlag accumulatedModelFlags = DivinityModelFlag.None;
        if (_root.Meshes != null)
        {
            foreach (var obj in _root.Meshes)
            {
                if (obj is Mesh mesh)
                {
                    if (mesh.IsSkinned())
                    {
                        accumulatedModelFlags |= DivinityModelFlag.Skinned;
                    }
                    if (mesh.BoneBindings != null && mesh.BoneBindings.Count == 1)
                    {
                        accumulatedModelFlags |= DivinityModelFlag.Rigid;
                    }
                    var componentNames = mesh.VertexComponentNames();
                    if (componentNames != null && componentNames.Any(c => c.StartsWith("DiffuseColor", StringComparison.OrdinalIgnoreCase)))
                    {
                        accumulatedModelFlags |= DivinityModelFlag.HasColor;
                    }
                    if (accumulatedModelFlags == DivinityModelFlag.None)
                    {
                        hasUndeterminedModelTypes = true;
                    }
                }
            }
        }
        MeshRigidEnabled = hasUndeterminedModelTypes;
        MeshClothEnabled = hasUndeterminedModelTypes;
        MeshProxyEnabled = hasUndeterminedModelTypes;
        MeshRigidChecked = accumulatedModelFlags.IsRigid();
        MeshClothChecked = accumulatedModelFlags.IsCloth();
        MeshProxyChecked = accumulatedModelFlags.IsMeshProxy();
        UpdateExportableObjects();
        UpdateResourceFormats();
        var resourceFormatsControl = Avalonia.LogicalTree.LogicalExtensions.FindLogicalDescendantOfType<ExportItemSelection>(this, false);

        if (resourceFormatsControl != null)
        {
            var selectedItemData = ExportableObjects.Select(x => (x.Name, x.Type, "Automatic")).ToList();
            resourceFormatsControl.PopulateFromSource(selectedItemData);
        }
        SaveOutputBtnEnabled = true;
    }
    private void UpdateExporterSettings(ExporterOptions settings)
    {
        UpdateCommonExporterSettings(settings);
        if (Settings?.GR2 != null)
        {
            settings.InputPath = Settings.GR2.InputPath;
            settings.InputFormat = GR2Utils.PathExtensionToModelFormat(settings.InputPath);
            settings.OutputPath = Settings.GR2.OutputPath;
            settings.OutputFormat = GR2Utils.PathExtensionToModelFormat(settings.OutputPath);
        }
        foreach (var item in ExportableObjects.Where(x => !x.IsChecked))
        {
            switch (item.Type)
            {
                case "Model": settings.DisabledModels.Add(item.Name); break;
                case "Skeleton": settings.DisabledSkeletons.Add(item.Name); break;
                case "Animation": settings.DisabledAnimations.Add(item.Name); break;
            }
        }
    }
    private void UpdateCommonExporterSettings(ExporterOptions settings)
    {
        if (Settings == null) return;
        Game game = (Game)Settings.SelectedGame;
        settings.FlipUVs = FlipUVs;
        settings.BuildDummySkeleton = BuildDummySkeletonChecked;
        settings.ApplyBasisTransforms = ApplyBasisTransforms;
        settings.FlipMesh = FlipMeshes;
        settings.MirrorSkeleton = MirrorSkeletons;
        settings.LoadGameSettings(game);
        settings.ModelType = 0;
        if (MeshRigidChecked) settings.ModelType |= (uint)DivinityModelFlag.Rigid;
        if (MeshClothChecked) settings.ModelType |= (uint)DivinityModelFlag.Cloth;
        if (MeshProxyChecked) settings.ModelType |= (uint)(DivinityModelFlag.MeshProxy | DivinityModelFlag.HasProxyGeometry);
        settings.ConformGR2Path = ConformToOriginalChecked && !string.IsNullOrEmpty(Settings.GR2.ConformPath)
        ? Settings.GR2.ConformPath
        : null;
        settings.ConformSkeletonsCopy = ConformCopySkeletonsEnabled;
    }
    private PropertyChangedEventHandler? _localPropertyChanged;
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => _localPropertyChanged += value;
        remove => _localPropertyChanged -= value;
    }
    private void InvokeLocalProperty(string propertyName) => _localPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
public class PaneToolkitRelayCommand(Action<object?> execute, Func<bool>? canExecute = null) : ICommand
{
    private readonly Action<object?> _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    private readonly Func<bool>? _canExecute = canExecute;
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute == null || _canExecute.Invoke();
    public void Execute(object? parameter) => _execute(parameter);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
