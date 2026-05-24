using Avalonia.Markup.Xaml;
using ReactiveUI;
using ReactiveUI.Avalonia;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace LSTools.DivineGUI;

public partial class ExportItemSelection : ReactiveUserControl<ExportItemSelection>, INotifyPropertyChanged
{
    private ExportItemModel? _selectedRow;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ExportItemModel> Items { get; } = [];

    public ExportItemModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (EqualityComparer<ExportItemModel?>.Default.Equals(_selectedRow, value)) return;

            _selectedRow = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedRow)));
        }
    }

    public ICommand RowSelectionChangedCommand { get; }

    public ExportItemSelection()
    {
        InitializeComponent();
        DataContext = this;

        RowSelectionChangedCommand = ReactiveCommand.Create<ExportItemModel?>(item =>
        {
            if (item != null)
            {
                SelectedRow = item;
            }
        });
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public void PopulateFromSource(IEnumerable<(string Name, string Type, string CurrentFormat)> sourceItems)
    {
        Items.Clear();

        string[] meshFormats = ["Automatic", "GR2", "DAE", "OBJ", "FBX", "GLTF"];
        string[] dynamicCurveTypes =
        [
            "Automatic",
            "Constant32BitKeyframe",
            "Constant64BitKeyframe",
            "Linear32BitKeyframe",
            "Linear64BitKeyframe",
            "CubicBezier32BitKeyframe",
            "CubicBezier64BitKeyframe"
        ];

        foreach (var (name, type, currentFormat) in sourceItems)
        {
            string[] allowedFormats = type switch
            {
                "Mesh" => meshFormats,
                "Position Track" or "Rotation Track" or "Scale/Shear Track" => dynamicCurveTypes,
                _ => ["Automatic"]
            };

            Items.Add(new ExportItemModel
            {
                Name = name,
                Type = type,
                SelectedFormat = allowedFormats.Contains(currentFormat) ? currentFormat : "Automatic",
                AvailableFormats = allowedFormats
            });
        }
    }
}

public class ExportItemModel : ReactiveObject
{
    private string _name = string.Empty;
    private string _type = string.Empty;
    private string _selectedFormat = "Automatic";
    private string[] _availableFormats = [];

    public string Name { get => _name; set => this.RaiseAndSetIfChanged(ref _name, value); }
    public string Type { get => _type; set => this.RaiseAndSetIfChanged(ref _type, value); }
    public string SelectedFormat { get => _selectedFormat; set => this.RaiseAndSetIfChanged(ref _selectedFormat, value); }
    public string[] AvailableFormats { get => _availableFormats; set => this.RaiseAndSetIfChanged(ref _availableFormats, value); }
}