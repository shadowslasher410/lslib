using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LSLib.LS;
using LSLib.LS.Enums;
using LSLib.LS.Resources.LSF;
using LSLib.LS.Story;
using LSLib.LS.Story.Compiler; // For handling the Diagnostic collection context

namespace LSTools.DivineGUI;

public record DumperProgressEventArgs(int Percentage, string StatusText);

public class DebugDumperTask
{
    private Package? _savePackage;
    private Resource? _saveMeta;
    private Resource? _saveGlobals;
    private Story? _saveStory;

    public Game GameVersion { get; set; }
    public string SaveFilePath { get; set; } = string.Empty;
    public string ExtractionPath { get; set; } = string.Empty;
    public string DataDumpPath { get; set; } = string.Empty;
    public bool ExtractAll { get; set; } = true;
    public bool ConvertToLsx { get; set; } = true;
    public bool DumpModList { get; set; } = true;
    public bool DumpGlobalVars { get; set; } = true;
    public bool DumpCharacterVars { get; set; } = true;
    public bool DumpItemVars { get; set; } = true;
    public bool IncludeDeletedVars { get; set; } = false;
    public bool IncludeLocalScopes { get; set; } = false;
    public bool DumpStoryDatabases { get; set; } = true;
    public bool DumpStoryGoals { get; set; } = true;
    public bool IncludeUnnamedDatabases { get; set; } = false;

    // TARGET EXTRACTION HOOK: Passes collected file logs, data validations, or errors back to the DataGrid collection
    public List<Diagnostic> TaskDiagnostics { get; } = [];

    public event Action<DumperProgressEventArgs>? ProgressUpdated;

    private void ReportProgress(int percentage, string statusText) =>
        ProgressUpdated?.Invoke(new DumperProgressEventArgs(percentage, statusText));

    private void DoExtractPackage()
    {
        ArgumentNullException.ThrowIfNull(_savePackage);

        var packager = new Packager
        {
            ProgressUpdate = (file, numerator, denominator) =>
                ReportProgress(5 + (int)(denominator == 0 ? 0 : numerator * 15 / denominator), $"Extracting: {file}")
        };

        packager.UncompressPackage(_savePackage, ExtractionPath);
        TaskDiagnostics.Add(new Diagnostic(null, MessageLevel.Info, "PAK01", $"Extracted complete payload successfully to {ExtractionPath}"));
    }

    private void DoLsxConversion()
    {
        ArgumentNullException.ThrowIfNull(_savePackage);

        var conversionParams = ResourceConversionParameters.FromGameVersion(GameVersion);
        var loadParams = ResourceLoadParameters.FromGameVersion(GameVersion);

        var lsfList = _savePackage.Files.Where(p => p.Name.EndsWith(".lsf", StringComparison.OrdinalIgnoreCase)).ToList();
        var numProcessed = 0;

        foreach (var lsf in lsfList)
        {
            try
            {
                var lsfPath = Path.Combine(ExtractionPath, lsf.Name);
                string baseName = lsf.Name.EndsWith(".lsf", StringComparison.OrdinalIgnoreCase) ? lsf.Name[..^4] : lsf.Name;
                var lsxPath = Path.Combine(ExtractionPath, $"{baseName}.lsx");

                int calculatedProgress = lsfList.Count == 0 ? 0 : numProcessed * 30 / lsfList.Count;
                ReportProgress(20 + calculatedProgress, $"Converting to LSX: {lsf.Name}");

                var resource = ResourceUtils.LoadResource(lsfPath, ResourceFormat.LSF, loadParams);
                ResourceUtils.SaveResource(resource, lsxPath, ResourceFormat.LSX, conversionParams);
                numProcessed++;
            }
            catch (Exception ex)
            {
                // Logs any localized serialization error cleanly into the grid display
                TaskDiagnostics.Add(new Diagnostic(null, MessageLevel.Warning, "CONV02", $"Failed transforming '{lsf.Name}': {ex.Message}"));
            }
        }
    }

    private Resource LoadPackagedResource(string path)
    {
        ArgumentNullException.ThrowIfNull(_savePackage);

        var fileInfo = _savePackage.Files.FirstOrDefault(p => p.Name.Equals(path, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Could not locate file in package: '{path}'");

        using var rsrcStream = fileInfo.CreateContentReader();
        using var rsrcReader = new LSFReader(rsrcStream);
        return rsrcReader.Read();
    }

    private void DumpMods(string outputPath)
    {
        ArgumentNullException.ThrowIfNull(_saveMeta);

        using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(outputStream);

        if (!_saveMeta.Regions.TryGetValue("MetaData", out var metaRegion)) return;
        if (!metaRegion.Children.TryGetValue("MetaData", out var metaChildrenList) || metaChildrenList.Count == 0) return;

        var meta = metaChildrenList[0];
        if (!meta.Children.TryGetValue("ModuleSettings", out var settingsList) || settingsList.Count == 0) return;
        if (!settingsList[0].Children.TryGetValue("Mods", out var modsList) || modsList.Count == 0) return;
        if (!modsList[0].Children.TryGetValue("ModuleShortDesc", out var moduleDescs)) return;

        foreach (var modDesc in moduleDescs)
        {
            string folder = modDesc.Attributes.TryGetValue("Folder", out var fAttr) && fAttr.Value is string folderName ? folderName : string.Empty;
            string name = modDesc.Attributes.TryGetValue("Name", out var nAttr) && nAttr.Value is string modName ? modName : string.Empty;

            var version = modDesc.Attributes.TryGetValue("Version64", out var v64) && v64.Value is long packed64
                ? PackedVersion.FromInt64(packed64)
                : (modDesc.Attributes.TryGetValue("Version", out var v32) && v32.Value is int packed32
                    ? PackedVersion.FromInt32(packed32)
                    : PackedVersion.FromInt32(0));

            writer.WriteLine($"{name} (v{version.Major}.{version.Minor}.{version.Revision}.{version.Build}) @ {folder}");
        }
    }

    private void DumpVariables(string outputPath, bool globals, bool characters, bool items)
    {
        ArgumentNullException.ThrowIfNull(_saveGlobals);

        using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        var varDumper = new VariableDumper(outputStream)
        {
            IncludeDeletedVars = IncludeDeletedVars,
            IncludeLocalScopes = IncludeLocalScopes
        };

        if (varDumper.Load(_saveGlobals))
        {
            if (globals) varDumper.DumpGlobals();
            if (characters) varDumper.DumpCharacters();
            if (items) varDumper.DumpItems();
        }
    }

    private void DumpGoals()
    {
        ArgumentNullException.ThrowIfNull(_saveStory);

        ReportProgress(80, "Dumping story ...");
        string debugPath = Path.Combine(DataDumpPath, "GoalsDebug.log");
        using (var debugFile = new FileStream(debugPath, FileMode.Create, FileAccess.Write))
        using (var writer = new StreamWriter(debugFile))
        {
            _saveStory.DebugDump(writer);
        }

        ReportProgress(85, "Dumping story goals ...");
        string goalsPath = Path.Combine(DataDumpPath, "Goals");
        FileManager.TryToCreateDirectory(Path.Combine(goalsPath, "Dummy"));

        string unassignedPath = Path.Combine(goalsPath, "UNASSIGNED_RULES.txt");
        using (var goalFile = new FileStream(unassignedPath, FileMode.Create, FileAccess.Write))
        using (var writer = new StreamWriter(goalFile))
        {
            var dummyGoal = new Goal(_saveStory)
            {
                ExitCalls = [],
                InitCalls = [],
                ParentGoals = [],
                SubGoals = [],
                Name = "UNASSIGNED_RULES",
                Index = 0
            };
            dummyGoal.MakeScript(writer, _saveStory);
        }

        foreach (var goalEntry in _saveStory.Goals)
        {
            var goal = goalEntry.Value;
            string filePath = Path.Combine(goalsPath, $"{goal.Name}.txt");
            using var goalFile = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            using var writer = new StreamWriter(goalFile);
            goal.MakeScript(writer, _saveStory);
        }
    }

    private void RunTasks()
    {
        ArgumentNullException.ThrowIfNull(_savePackage);

        if (ExtractAll) DoExtractPackage();
        if (ConvertToLsx) DoLsxConversion();

        FileManager.TryToCreateDirectory(Path.Combine(DataDumpPath, "Dummy"));

        ReportProgress(50, "Loading meta.lsf ...");
        _saveMeta = LoadPackagedResource("meta.lsf");

        ReportProgress(52, "Loading globals.lsf ...");
        _saveGlobals = LoadPackagedResource("globals.lsf");

        if (DumpModList)
        {
            ReportProgress(60, "Dumping mod list ...");
            DumpMods(Path.Combine(DataDumpPath, "ModList.txt"));
        }

        ReportProgress(62, "Dumping variables ...");
        if (DumpGlobalVars) DumpVariables(Path.Combine(DataDumpPath, "GlobalVars.txt"), true, false, false);
        if (DumpCharacterVars) DumpVariables(Path.Combine(DataDumpPath, "CharacterVars.txt"), false, true, false);
        if (DumpItemVars) DumpVariables(Path.Combine(DataDumpPath, "ItemVars.txt"), false, false, true);

        ReportProgress(70, "Loading story ...");
        var storySave = _savePackage.Files.FirstOrDefault(p => p.Name.Equals("StorySave.bin", StringComparison.OrdinalIgnoreCase));

        using var storyStream = new MemoryStream();
        if (storySave != null)
        {
            using var bin = storySave.CreateContentReader();
            bin.CopyTo(storyStream);
        }
        else
        {
            if (_saveGlobals.Regions.TryGetValue("Story", out var storyRegion) &&
                storyRegion.Children.TryGetValue("Story", out var storyChildrenList) &&
storyChildrenList.Count > 0)
            {
                var storyNode = storyChildrenList[0];
                if (storyNode.Attributes.TryGetValue("Story", out var storyAttr) && storyAttr.Value is byte[] byteData)
                {
                    storyStream.Write(byteData, 0, byteData.Length);
                }
                else
                {
                    throw new InvalidDataException("Story element stream attribute could not be extracted or was not a valid byte array.");
                }
            }
            else
            {
                throw new InvalidDataException("The specified globals resource does not contain an active Story save node tree.");
            }
        }
        storyStream.Position = 0;
        _saveStory = StoryReader.Read(storyStream);
        if (DumpStoryGoals) DumpGoals();
        if (DumpStoryDatabases) DumpStoryDatabasesTask();
        ReportProgress(100, "Done");
        TaskDiagnostics.Add(new Diagnostic(null, MessageLevel.Info, "SUCCESS", "All requested data blocks dumped completely."));
    }
    private void DumpStoryDatabasesTask()
    {
        ArgumentNullException.ThrowIfNull(_saveStory);
        ReportProgress(90, "Dumping databases ...");
        var dbDumpPath = Path.Combine(DataDumpPath, "Databases.txt");
        using var dbDumpStream = new FileStream(dbDumpPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        var dbDumper = new DatabaseDumper(dbDumpStream)
        {
            DumpUnnamedDbs = IncludeUnnamedDatabases
        };
        dbDumper.DumpAll(_saveStory);
    }
    public async Task RunAsync()
    {
        TaskDiagnostics.Clear();
        if (string.IsNullOrWhiteSpace(SaveFilePath) || !File.Exists(SaveFilePath))
        {
            ReportProgress(0, "Execution Aborted: Source package target is blank or inaccessible.");
            TaskDiagnostics.Add(new Diagnostic(null, MessageLevel.Error, "FILE01", "The specified archive path is null or target file does not exist."));
            return;
        }
        ReportProgress(0, "Reading package metadata structure components...");
        await Task.Run(() =>
        {
            var packageReader = new PackageReader();
            try
            {
                _savePackage = packageReader.Read(SaveFilePath);
                bool hasGlobals = _savePackage.Files.Any(p => p.Name.Equals("globals.lsf", StringComparison.OrdinalIgnoreCase));
                if (!hasGlobals)
                {
                    throw new InvalidDataException("The specified package is not a valid savegame (globals.lsf not found).");
                }
                RunTasks();
            }
            catch (Exception ex)
            {
                ReportProgress(0, $"Processing Interrupted: {ex.Message}");
                TaskDiagnostics.Add(new Diagnostic(null, MessageLevel.Error, "CRIT01", ex.Message));
            }
            finally
            {
                _savePackage?.Dispose();
                _savePackage = null;
                _saveMeta = null;
                _saveGlobals = null;
                _saveStory = null;
            }
        });
    }
}