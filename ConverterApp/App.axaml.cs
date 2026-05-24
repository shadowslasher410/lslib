using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ReactiveUI;
using ReactiveUI.Avalonia;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;

namespace LSTools.DivineGUI;

public class BatchProcessItem : ReactiveObject
{
    private bool _isChecked;
    private string _name = string.Empty;
    private string _type = string.Empty;
    private string _batchStatusText = string.Empty;
    private double _batchProgressValue;

    public bool IsChecked { get => _isChecked; set => this.RaiseAndSetIfChanged(ref _isChecked, value); }
    public string Name { get => _name; set => this.RaiseAndSetIfChanged(ref _name, value); }
    public string Type { get => _type; set => this.RaiseAndSetIfChanged(ref _type, value); }

    public ConverterAppSettings Settings { get; set; } = new();

    public bool FlipUVs { get; set; }
    public bool MirrorSkeletons { get; set; }
    public bool ApplyBasisTransforms { get; set; }
    public bool MeshRigidChecked { get; set; }
    public bool MeshRigidEnabled { get; set; }
    public bool MeshClothChecked { get; set; }
    public bool MeshClothEnabled { get; set; }
    public bool MeshProxyChecked { get; set; }
    public bool MeshProxyEnabled { get; set; }

    public string BatchStatusText { get => _batchStatusText; set => this.RaiseAndSetIfChanged(ref _batchStatusText, value); }
    public double BatchProgressValue { get => _batchProgressValue; set => this.RaiseAndSetIfChanged(ref _batchProgressValue, value); }
}

public class MainSettingsProvider : ISettingsDataSource
{
    public ConverterAppSettings Settings { get; set; } = new();
}

public partial class App : Application
{
    public ObservableCollection<BatchProcessItem> BatchProcessItemsCollection { get; } = [];
    public ICommand? BrowsePathCommand { get; set; }
    public ICommand? SaveOutputCommand { get; set; }

    [STAThread]
    public static void Main(string[] args)
    {
        CultureInfo customCulture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
        customCulture.NumberFormat.NumberDecimalSeparator = ".";

        Thread.CurrentThread.CurrentCulture = customCulture;
        Thread.CurrentThread.CurrentUICulture = customCulture;
        CultureInfo.DefaultThreadCurrentCulture = customCulture;
        CultureInfo.DefaultThreadCurrentUICulture = customCulture;

        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI(rxui => { })
            .RegisterReactiveUIViewsFromEntryAssembly()
            .StartWithClassicDesktopLifetime(args);
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainSettingsContext = new MainSettingsProvider();
            desktop.MainWindow = new MainWindow(mainSettingsContext);
        }

        base.OnFrameworkInitializationCompleted();
    }
}