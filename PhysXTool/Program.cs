using PhysXTool;
using System.CommandLine;
using System.Text;

var inputArgument = new Argument<FileInfo>("input")
{
    Description = "Path to the input source file context (.bin or .xml)"
};

var outputArgument = new Argument<FileInfo>("output")
{
    Description = "Destination path for the compiled output resource payload (.bin or .xml)"
};

var rootCommand = new RootCommand("Osiris Physics Asset Compilation and Transformation Engine Tool")
{
    inputArgument,
    outputArgument
};

rootCommand.SetAction(parseResult =>
{
    FileInfo inputFile = parseResult.GetValue(inputArgument)!;
    FileInfo outputFile = parseResult.GetValue(outputArgument)!;

    string inputExt = inputFile.Extension.ToLowerInvariant();
    string outputExt = outputFile.Extension.ToLowerInvariant();

    if (inputExt is not (".bin" or ".xml") || outputExt is not (".bin" or ".xml"))
    {
        var originalColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("Error: Structural validation failure. Arguments must strictly match format extensions '.bin' or '.xml'.");
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }

        Environment.ExitCode = 1;
        return;
    }

    try
    {
        using var converter = new PhysXConverter();
        if (!converter.InitPhysX())
        {
            Console.Error.WriteLine("Failed to map initialization contexts onto underlying system runtimes.");
            Environment.ExitCode = 1;
            return;
        }

        bool inputIsXml = inputExt == ".xml";
        bool outputIsXml = outputExt == ".xml";

        Console.WriteLine($"Reading tracking resource file source: {inputFile.FullName}");

        byte[] inputBytes = File.ReadAllBytes(inputFile.FullName);

        PhysicsCollection collection = inputIsXml
            ? PhysXSaverConverter.LoadCollectionFromXmlString(File.ReadAllText(inputFile.FullName))
            : converter.LoadCollectionFromBinary(inputBytes);

        Console.WriteLine("Executing transformation pipeline operations...");

        byte[] outputBytes = outputIsXml
            ? Encoding.UTF8.GetBytes(PhysXSaveConverter.ExportCollectionToXmlString(collection))
            : converter.SaveCollectionToBinary(collection);

        Console.WriteLine($"Writing transformed asset destination file payload: {outputFile.FullName}");
        File.WriteAllBytes(outputFile.FullName, outputBytes);
        converter.ReleaseCollection(collection);

        Console.WriteLine("Pipeline translation task completed successfully.");
        Environment.ExitCode = 0;
    }
    catch (Exception ex)
    {
        var originalColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Fatal pipeline exception intercepted: {ex.Message}");
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }
        Environment.ExitCode = 1;
    }
});

return rootCommand.Parse(args).Invoke();