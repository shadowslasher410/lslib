using LSLib.LS.Enums;
using LSLib.LS.Resources.LSF;
using LSLib.LS.Story;

namespace LSLib.LS.Save;

public class SavegameHelpers : IDisposable
{
    private readonly Package _package;
    private bool _isDisposed;

    public SavegameHelpers(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var reader = new PackageReader();
        _package = reader.Read(path);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                _package.Dispose();
            }
            _isDisposed = true;
        }
    }

    public Resource LoadGlobals()
    {
        var globalsInfo = _package.Files.FirstOrDefault(p => string.Equals(p.Name, "globals.lsf", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("The specified package is not a valid savegame (globals.lsf not found)");
        using var rsrcStream = globalsInfo.CreateContentReader();
        using var rsrcReader = new LSFReader(rsrcStream);
        return rsrcReader.Read();
    }

    public static Story.Story LoadStory(Stream s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return StoryReader.Read(s);
    }

    public Story.Story LoadStory()
    {
        var storyInfo = _package.Files.FirstOrDefault(p => string.Equals(p.Name, "StorySave.bin", StringComparison.Ordinal));
        if (storyInfo is not null)
        {
            using var rsrcStream = storyInfo.CreateContentReader();
            return LoadStory(rsrcStream);
        }
        else
        {
            var globals = LoadGlobals();

            if (!globals.Regions.TryGetValue("Story", out var storyRegion) ||
                !storyRegion.Children.TryGetValue("Story", out var storyList) ||
                storyList.Count == 0)
            {
                throw new InvalidDataException("Malformed savegame database resource tree structures mapping: Region 'Story' or Node 'Story' was missing.");
            }

            var storyNode = storyList[0];
            if (!storyNode.Attributes.TryGetValue("Story", out var storyAttr) || storyAttr?.Value is not byte[] storyBytes)
            {
                throw new InvalidOperationException("Cannot proceed with missing, empty, or un-parsable Story node attribute streams.");
            }

            var storyStream = new MemoryStream(storyBytes);
            return LoadStory(storyStream);
        }
    }

    public byte[] ResaveStoryToGlobals(Story.Story story, ResourceConversionParameters conversionParams)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(conversionParams);

        var globals = LoadGlobals();

        using (var storyStream = new MemoryStream())
        {
            StoryWriter.Write(storyStream, story, true);

            if (!globals.Regions.TryGetValue("Story", out var storyRegion) ||
                !storyRegion.Children.TryGetValue("Story", out var storyList) ||
                storyList.Count == 0)
            {
                throw new InvalidDataException("Malformed savegame database resource tree structure: Region 'Story' or Node 'Story' was missing.");
            }

            var storyNode = storyList[0];
            if (!storyNode.Attributes.TryGetValue("Story", out var storyAttr) || storyAttr is null)
            {
                storyAttr = new NodeAttribute(AttributeType.ScratchBuffer);
                storyNode.Attributes["Story"] = storyAttr;
            }

            storyAttr.Value = storyStream.ToArray();
        }
        var rewrittenStream = new MemoryStream();
        var rsrcWriter = new LSFWriter(rewrittenStream)
        {
            Version = conversionParams.LSF,
            MetadataFormat = LSFMetadataFormat.None
        };

        rsrcWriter.Write(globals);
        rewrittenStream.Seek(0, SeekOrigin.Begin);
        return rewrittenStream.ToArray();
    }

    public void ResaveStory(Story.Story story, Game game, string path)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentException.ThrowIfNullOrEmpty(path);
        var conversionParams = ResourceConversionParameters.FromGameVersion(game);

        var build = new PackageBuildData
        {
            Version = conversionParams.PAKVersion,
            Compression = CompressionMethod.Zlib,
            CompressionLevel = LSCompressionLevel.Default
        };

        var storyBin = _package.Files.FirstOrDefault(p => string.Equals(p.Name, "StorySave.bin", StringComparison.Ordinal));
        if (storyBin is null)
        {
            byte[] globals = ResaveStoryToGlobals(story, conversionParams);
            var globalsLsf = _package.Files.FirstOrDefault(p => string.Equals(p.Name, "globals.lsf", StringComparison.OrdinalIgnoreCase));
            string targetName = globalsLsf?.Name ?? "globals.lsf";

            var globalsRepacked = PackageBuildInputFile.CreateFromBlob(globals, targetName);
            build.Files.Add(globalsRepacked);

            foreach (var file in _package.Files.Where(x => !string.Equals(x.Name, "globals.lsf", StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = file.CreateContentReader();
                using var unpacked = new MemoryStream();
                stream.CopyTo(unpacked);

                build.Files.Add(PackageBuildInputFile.CreateFromBlob(unpacked.ToArray(), file.Name));
            }
        }
        else
        {
            var storyStream = new MemoryStream();
            StoryWriter.Write(storyStream, story, true);

            var storyRepacked = PackageBuildInputFile.CreateFromBlob(storyStream.ToArray(), "StorySave.bin");
            build.Files.Add(storyRepacked);

            foreach (var file in _package.Files.Where(x => !string.Equals(x.Name, "StorySave.bin", StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = file.CreateContentReader();
                using var unpacked = new MemoryStream();
                stream.CopyTo(unpacked);

                build.Files.Add(PackageBuildInputFile.CreateFromBlob(unpacked.ToArray(), file.Name));
            }
        }

        using var packageWriter = PackageWriterFactory.Create(build, path);
        packageWriter.Write();
    }
}