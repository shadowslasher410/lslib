using System.Text.RegularExpressions;
using LSLib.LS.Story.Compiler;

namespace LSLib.LS;

public class ModInfo(string name)
{
    public string Name { get; set; } = name ?? throw new ArgumentNullException(nameof(name));

    public string ModsPath { get; set; } = string.Empty;
    public string PublicPath { get; set; } = string.Empty;

    public string Meta { get; set; } = string.Empty;
    public List<string> Scripts { get; set; } = [];
    public List<string> Stats { get; set; } = [];
    public List<string> Globals { get; set; } = [];
    public List<string> LevelObjects { get; set; } = [];
    public string OrphanQueryIgnoreList { get; set; } = string.Empty;
    public string StoryHeaderFile { get; set; } = string.Empty;
    public string TypeCoercionWhitelistFile { get; set; } = string.Empty;
    public string ModifiersFile { get; set; } = string.Empty;
    public string ValueListsFile { get; set; } = string.Empty;
    public string ActionResourcesFile { get; set; } = string.Empty;
    public string ActionResourceGroupsFile { get; set; } = string.Empty;
    public List<string> TagFiles { get; set; } = [];
}

public class ModResources : IDisposable
{
    public Dictionary<string, ModInfo> Mods { get; set; } = new(StringComparer.Ordinal);
    public List<Package> LoadedPackages { get; set; } = [];

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var p in LoadedPackages)
            {
                p?.Dispose();
            }
            LoadedPackages.Clear();
        }
    }
}

public partial class ModPathVisitor(ModResources resources, VFS fs)
{
    public static readonly Regex archivePartRe = ArchivePartRegex();

    public const string ModsPath = "Mods";
    public const string PublicPath = "Public";

    public ModResources Resources { get; init; } = resources ?? throw new ArgumentNullException(nameof(resources));

    public bool CollectStoryGoals { get; set; }
    public bool CollectStats { get; set; }
    public bool CollectGlobals { get; set; }
    public bool CollectLevels { get; set; }
    public bool CollectGuidResources { get; set; }
    public TargetGame Game { get; set; } = TargetGame.DOS2;
    public VFS FS { get; set; } = fs ?? throw new ArgumentNullException(nameof(fs));

    private ModInfo GetMod(string modName)
    {
        if (!Resources.Mods.TryGetValue(modName, out var mod))
        {
            mod = new ModInfo(modName);
            Resources.Mods[modName] = mod;
        }

        return mod;
    }

    public void AddGlobalsToMod(string modName, string path) => GetMod(modName).Globals.Add(path);

    public void AddLevelObjectsToMod(string modName, string path) => GetMod(modName).LevelObjects.Add(path);

    private void DiscoverModGoals(ModInfo mod)
    {
        var goalPath = Path.Join(mod.ModsPath, "Story/RawFiles/Goals");
        if (!FS.DirectoryExists(goalPath)) return;

        var goalFiles = FS.EnumerateFiles(goalPath, false, p => Path.GetExtension(p) == ".txt");

        foreach (var goalFile in goalFiles)
        {
            if (goalFile is not null) mod.Scripts.Add(goalFile);
        }
    }

    private void DiscoverModStats(ModInfo mod)
    {
        var statsPath = Path.Join(mod.PublicPath, "Stats/Generated/Data");
        if (!FS.DirectoryExists(statsPath)) return;

        var statFiles = FS.EnumerateFiles(statsPath, false, p => Path.GetExtension(p) == ".txt");

        foreach (var statFile in statFiles)
        {
            if (statFile is not null) mod.Stats.Add(statFile);
        }

        var treasurePath = Path.Join(mod.PublicPath, "Stats/Generated/TreasureTable.txt");
        if (FS.FileExists(treasurePath))
        {
            mod.Stats.Add(treasurePath);
        }
    }

    private void DiscoverModStatsStructure(ModInfo mod)
    {
        var modifiersPath = Path.Join(mod.PublicPath, "Stats/Generated/Structure/Modifiers.txt");
        if (FS.FileExists(modifiersPath))
        {
            mod.ModifiersFile = modifiersPath;
        }

        var valueListsPath = Path.Join(mod.PublicPath, "Stats/Generated/Structure/Base/ValueLists.txt");
        if (FS.FileExists(valueListsPath))
        {
            mod.ValueListsFile = valueListsPath;
        }
    }

    private void DiscoverModGuidResources(ModInfo mod)
    {
        var actionResGrpPath = Path.Join(mod.PublicPath, "ActionResourceGroupDefinitions/ActionResourceGroupDefinitions.lsx");
        if (FS.FileExists(actionResGrpPath))
        {
            mod.ActionResourceGroupsFile = actionResGrpPath;
        }

        var actionResPath = Path.Join(mod.PublicPath, "ActionResourceDefinitions/ActionResourceDefinitions.lsx");
        if (FS.FileExists(actionResPath))
        {
            mod.ActionResourcesFile = actionResPath;
        }

        var tagPath = Path.Join(mod.PublicPath, "Tags");
        if (FS.DirectoryExists(tagPath))
        {
            var tagFiles = FS.EnumerateFiles(tagPath, false, p => Path.GetExtension(p) == ".lsf");

            foreach (var tagFile in tagFiles)
            {
                if (tagFile is not null) mod.TagFiles.Add(tagFile);
            }
        }
    }

    private void DiscoverModGlobals(ModInfo mod)
    {
        var globalsPath = Path.Join(mod.ModsPath, "Globals");
        if (!FS.DirectoryExists(globalsPath)) return;

        var globalFiles = FS.EnumerateFiles(globalsPath, false, p => Path.GetExtension(p) == ".lsf");

        foreach (var globalFile in globalFiles)
        {
            if (globalFile is not null) mod.Globals.Add(globalFile);
        }
    }

    private void DiscoverModLevelObjects(ModInfo mod)
    {
        var levelsPath = Path.Join(mod.ModsPath, "Levels");
        if (!FS.DirectoryExists(levelsPath)) return;

        var levelFiles = FS.EnumerateFiles(levelsPath, false, p => Path.GetExtension(p) == ".lsf");

        foreach (var levelFile in levelFiles)
        {
            if (levelFile is not null) mod.LevelObjects.Add(levelFile);
        }
    }

    public void DiscoverModDirectory(ModInfo mod)
    {
        ArgumentNullException.ThrowIfNull(mod);

        if (CollectStoryGoals)
        {
            DiscoverModGoals(mod);

            var headerPath = Path.Join(mod.ModsPath, "Story/RawFiles/story_header.div");
            if (FS.FileExists(headerPath))
            {
                mod.StoryHeaderFile = headerPath;
            }

            var orphanQueryIgnoresPath = Path.Join(mod.ModsPath, "Story/story_orphanqueries_ignore_local.txt");
            if (FS.FileExists(orphanQueryIgnoresPath))
            {
                mod.OrphanQueryIgnoreList = orphanQueryIgnoresPath;
            }

            var typeCoercionWhitelistPath = Path.Join(mod.ModsPath, "Story/RawFiles/TypeCoercionWhitelist.txt");
            if (FS.FileExists(typeCoercionWhitelistPath))
            {
                mod.TypeCoercionWhitelistFile = typeCoercionWhitelistPath;
            }
        }

        if (CollectStats)
        {
            DiscoverModStats(mod);
            DiscoverModStatsStructure(mod);
        }

        if (CollectGuidResources)
        {
            DiscoverModGuidResources(mod);
        }

        if (CollectGlobals)
        {
            DiscoverModGlobals(mod);
        }

        if (CollectLevels)
        {
            DiscoverModLevelObjects(mod);
        }
    }

    public void DiscoverMods()
    {
        var modPaths = FS.EnumerateDirectories(ModsPath);

        foreach (var modPath in modPaths)
        {
            if (string.IsNullOrEmpty(modPath)) continue;

            var modName = Path.GetFileName(modPath);
            var metaPath = Path.Combine(modPath, "meta.lsx");

            if (FS.FileExists(metaPath))
            {
                var mod = GetMod(modName);
                mod.ModsPath = modPath;
                mod.PublicPath = Path.Combine(PublicPath, Path.GetFileName(modPath));
                mod.Meta = metaPath;

                DiscoverModDirectory(mod);
            }
        }
    }

    public void Discover() => DiscoverMods();

    [GeneratedRegex(@"^(.*)_[0-9]+\.pak$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex ArchivePartRegex();
}

public class GameDataContext
{
    public VFS FS { get; set; }
    public ModResources Resources { get; set; }

    public GameDataContext(string path, TargetGame game = TargetGame.BG3, bool excludeAssets = true, bool loadUnpackedFiles = true)
    {
        FS = new VFS();
        FS.AttachGameDirectory(path, excludeAssets, loadUnpackedFiles);
        FS.FinishBuild();

        Resources = new ModResources();
        var visitor = new ModPathVisitor(Resources, FS)
        {
            Game = game,
            CollectStoryGoals = true,
            CollectGlobals = false,
            CollectLevels = false
        };
        visitor.Discover();
    }
}
