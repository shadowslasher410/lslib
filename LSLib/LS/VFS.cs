namespace LSLib.LS;

public class VFSDirectory
{
    public string Path { get; set; } = string.Empty;
    public Dictionary<string, VFSDirectory>? Dirs { get; set; }
    public Dictionary<string, PackagedFileInfo>? Files { get; set; }

    public VFSDirectory GetOrAddDirectory(string absolutePath, string name)
    {
        Dirs ??= [];

        if (!Dirs.TryGetValue(name, out var dir))
        {
            dir = new VFSDirectory { Path = absolutePath };
            Dirs[name] = dir;
        }

        return dir;
    }

    public bool TryGetDirectory(string name, out VFSDirectory? dir)
    {
        if (Dirs?.TryGetValue(name, out dir) == true)
        {
            return true;
        }

        dir = null;
        return false;
    }

    public void AddFile(string name, PackagedFileInfo file)
    {
        Files ??= [];

        if (!Files.TryGetValue(name, out var curFile) || curFile.Package.Metadata.Priority < file.Package.Metadata.Priority)
        {
            Files[name] = file;
        }
    }

    public bool TryGetFile(string name, out PackagedFileInfo? file)
    {
        if (Files?.TryGetValue(name, out file) == true)
        {
            return true;
        }

        file = null;
        return false;
    }
}

public class VFS : IDisposable
{
    private readonly List<Package> _packages = [];
    private string? _rootDir;
    private readonly VFSDirectory _root = new();

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var package in _packages)
        {
            package.Dispose();
        }
    }

    public void AttachRoot(string path) => _rootDir = path;
    public void DetachRoot() => _rootDir = null;


    public void AttachGameDirectory(string gameDataPath, bool excludeAssets = true, bool loadUnpackedFiles = true)
    {
        if (loadUnpackedFiles)
        {
            AttachRoot(gameDataPath);
        }

        // List of packages we won't ever load
        // These packages don't contain any mod resources, but have a large
        // file table that makes loading unneccessarily slow.
        HashSet<string> packageBlacklist = [];

        if (excludeAssets)
        {
            packageBlacklist = [
                "Assets.pak",
                "Effects.pak",
                "Engine.pak",
                "EngineShaders.pak",
                "Game.pak",
                "GamePlatform.pak",
                "Gustav_NavCloud.pak",
                "Gustav_Textures.pak",
                "Gustav_Video.pak",
                "Icons.pak",
                "LowTex.pak",
                "Materials.pak",
                "Minimaps.pak",
                "Models.pak",
                "PsoCache.pak",
                "SharedSoundBanks.pak",
                "SharedSounds.pak",
                "Textures.pak",
                "VirtualTextures.pak",
                // Localization
                "English_Animations.pak",
                "VoiceMeta.pak",
                "Voice.pak"
            ];
        }

        foreach (var path in Directory.GetFiles(gameDataPath, "*.pak"))
        {
            var baseName = Path.GetFileName(path);
            if (!packageBlacklist.Contains(baseName) && !ModPathVisitor.archivePartRe.IsMatch(baseName)) // Don't load 2nd, 3rd, ... parts of a multi-part archive
            {
                AttachPackage(path);
            }
        }

        var localizationDir = Path.Join(gameDataPath, "Localization");
        if (Directory.Exists(localizationDir))
        {
            foreach (var path in Directory.GetFiles(localizationDir, "*.pak"))
            {
                var baseName = Path.GetFileName(path);
                if (!packageBlacklist.Contains(baseName) && !ModPathVisitor.archivePartRe.IsMatch(baseName))// Don't load 2nd, 3rd, ... parts of a multi-part archive
                {
                    AttachPackage(path);
                }
            }
        }
    }

    public void AttachPackage(string path)
    {
        PackageReader reader = new();
        var package = reader.Read(path);
        _packages.Add(package);
    }

    public void FinishBuild()
    {
        foreach (var package in _packages)
        {
            foreach (var file in package.Files)
            {
                TryAddFile(file);
            }
        }
    }

    private void TryAddFile(PackagedFileInfo file)
    {
        ReadOnlySpan<char> pathSpan = file.Name.AsSpan();
        var node = _root;
        int currentPos = 0;

        // Modern Allocation-free deep folder initialization via Span indexing
        while (currentPos < pathSpan.Length)
        {
            int nextSlash = pathSpan[currentPos..].IndexOf('/');
            if (nextSlash >= 0)
            {
                int endPos = currentPos + nextSlash;
                ReadOnlySpan<char> absoluteSegment = pathSpan[..endPos];
                ReadOnlySpan<char> relativeSegment = pathSpan[currentPos..endPos];

                node = node.GetOrAddDirectory(absoluteSegment.ToString(), relativeSegment.ToString());
                currentPos = endPos + 1;
            }
            else
            {
                node.AddFile(pathSpan[currentPos..].ToString(), file);
                break;
            }
        }
    }

    public VFSDirectory? FindVFSDirectory(string path)
    {
        ReadOnlySpan<char> pathSpan = path.AsSpan();
        var node = _root;
        int currentPos = 0;

        while (currentPos < pathSpan.Length)
        {
            int nextSlash = pathSpan[currentPos..].IndexOf('/');
            if (nextSlash >= 0)
            {
                int endPos = currentPos + nextSlash;
                if (!node.TryGetDirectory(pathSpan[currentPos..endPos].ToString(), out node) || node is null)
                {
                    return null;
                }
                currentPos = endPos + 1;
            }
            else
            {
                return node.TryGetDirectory(pathSpan[currentPos..].ToString(), out var targetNode) ? targetNode : null;
            }
        }
        return node;
    }

    public bool DirectoryExists(string path)
    {
        if (FindVFSDirectory(Canonicalize(path)) is not null) return true;
        return _rootDir is not null && Directory.Exists(Path.Combine(_rootDir, path));
    }

    public PackagedFileInfo? FindVFSFile(string path)
    {
        ReadOnlySpan<char> pathSpan = path.AsSpan();
        var node = _root;
        int currentPos = 0;

        while (currentPos < pathSpan.Length)
        {
            int nextSlash = pathSpan[currentPos..].IndexOf('/');
            if (nextSlash >= 0)
            {
                int endPos = currentPos + nextSlash;
                if (!node.TryGetDirectory(pathSpan[currentPos..endPos].ToString(), out node) || node is null)
                {
                    return null;
                }
                currentPos = endPos + 1;
            }
            else
            {
                return node.TryGetFile(pathSpan[currentPos..].ToString(), out var file) ? file : null;
            }
        }
        return null;
    }

    public static string Canonicalize(string path) => path.Replace('\\', '/');

    public bool FileExists(string path)
    {
        if (FindVFSFile(Canonicalize(path)) is not null) return true;
        return _rootDir is not null && File.Exists(Path.Combine(_rootDir, path));
    }

    public List<string> EnumerateFiles(string path, bool recursive = false) => EnumerateFiles(path, recursive, _ => true);

    public List<string> EnumerateFiles(string path, bool recursive, Func<string, bool> filter)
    {
        List<string> results = [];
        EnumerateFiles(results, path, recursive, filter);
        return results;
    }

    public void EnumerateFiles(List<string> results, string path, bool recursive, Func<string, bool> filter)
    {
        var dir = FindVFSDirectory(Canonicalize(path));

        if (dir?.Files is not null)
        {
            foreach (var file in dir.Files.Values)
            {
                if (filter(file.Name))
                {
                    results.Add(file.Name);
                }
            }
        }

        if (recursive && dir?.Dirs is not null)
        {
            foreach (var subDir in dir.Dirs.Values)
            {
                EnumerateFiles(results, subDir.Path, true, filter);
            }
        }

        if (_rootDir is not null)
        {
            var fsDir = Path.Join(_rootDir, path);
            if (Directory.Exists(fsDir))
            {
                var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (var file in Directory.EnumerateFiles(fsDir, "*", option))
                {
                    if (filter(file))
                    {
                        results.Add(Path.GetRelativePath(_rootDir, file));
                    }
                }
            }
        }
    }

    public List<string> EnumerateDirectories(string path)
    {
        List<string> results = [];
        EnumerateDirectories(results, path);
        return results;
    }

    public void EnumerateDirectories(List<string> results, string path)
    {
        var dir = FindVFSDirectory(Canonicalize(path));
        if (dir?.Dirs is not null)
        {
            foreach (var subdir in dir.Dirs.Values)
            {
                results.Add(subdir.Path);
            }
        }

        if (_rootDir is not null)
        {
            var fsDir = Path.Join(_rootDir, path);
            if (Directory.Exists(fsDir))
            {
                foreach (var subdir in Directory.EnumerateDirectories(fsDir))
                {
                    results.Add(Path.GetRelativePath(_rootDir, subdir));
                }
            }
        }
    }

    private static void EnumerateFiles(List<string> results, VFSDirectory dir, bool recursive, Func<string, bool> filter)
    {
        if (dir.Files is not null)
        {
            foreach (var file in dir.Files.Values)
            {
                if (!file.IsDeletion() && filter(file.Name))
                {
                    results.Add(file.Name);
                }
            }
        }

        if (recursive && dir.Dirs is not null)
        {
            foreach (var subdir in dir.Dirs.Values)
            {
                EnumerateFiles(results, subdir, recursive, filter);
            }
        }
    }

    public bool TryOpenFromVFS(string path, out Stream? stream)
    {
        var file = FindVFSFile(Canonicalize(path));
        if (file is not null && !file.IsDeletion())
        {
            stream = file.CreateContentReader();
            return true;
        }

        stream = null;
        return false;
    }

    public bool TryOpen(string path, out Stream? stream)
    {
        if (TryOpenFromVFS(path, out stream)) return true;

        if (_rootDir is not null)
        {
            var realPath = Path.Join(_rootDir, path);
            if (File.Exists(realPath))
            {
                stream = File.OpenRead(realPath);
                return true;
            }
        }

        stream = null;
        return false;
    }

    public Stream Open(string path)
    {
        if (!TryOpen(path, out var stream) || stream is null)
        {
            throw new FileNotFoundException($"File not found in VFS: {path}", path);
        }

        return stream;
    }
}