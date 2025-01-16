using System.CommandLine;
using System.Text;

var rootCommand = new RootCommand("A tool to bundle the contents of a few files into one file");

var bundleCommand = new Command("bundle", "Combines the contents of a few files into one file");
bundleCommand.AddAlias("b");

// Define options
var languageOption = CreateLanguageOption();
var outputOption = new Option<FileInfo>("--output", "File path and name");
outputOption.AddAlias("-o");
var noteOption = new Option<bool>("--note", "Include a comment with the source file name and relative path before each file content");
noteOption.AddAlias("-n");
var sortOption = new Option<string>("--sort", description: "Sort order for files: 'name' (default) or 'type'.", getDefaultValue: () => "name");
sortOption.AddAlias("-s");
var removeEmptyLinesOption = new Option<bool>(aliases: new[] { "--remove-empty-lines", "-r", "--rel", "--rm-lines" }, description: "Removes empty lines from the source files before bundling.");
var authorOption = new Option<string>("--author", "Specify the author name to include in the bundle file");
authorOption.AddAlias("-a");

bundleCommand.AddOption(languageOption);
bundleCommand.AddOption(outputOption);
bundleCommand.AddOption(noteOption);
bundleCommand.AddOption(sortOption);
bundleCommand.AddOption(removeEmptyLinesOption);
bundleCommand.AddOption(authorOption);

bundleCommand.SetHandler((string[] languages, FileInfo output, bool note, string sort, bool removeEmptyLines, string author) =>
{
    try
    {
        var selectedExtensions = GetSelectedExtensions(languages);

        if (!selectedExtensions.Any())
        {
            throw new Exception("No valid languages selected.");
        }

        var ignoredPatterns = LoadIgnoredPatterns();
        var files = GetMatchingFiles(selectedExtensions, ignoredPatterns);

        if (!files.Any())
        {
            Console.WriteLine("No matching files found for the selected languages.");
            return;
        }

        files = SortFiles(files, sort);
        WriteToBundleFile(output, files, note, author, removeEmptyLines);
    }
    catch (FileNotFoundException)
    {
        Console.WriteLine("ERROR: File not found.");
    }
    catch (IOException ex)
    {
        Console.WriteLine($"ERROR: I/O error occurred: {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR: An unexpected error occurred: {ex.Message}");
    }
}, languageOption, outputOption, noteOption, sortOption, removeEmptyLinesOption, authorOption);

var createRspCommand = new Command("create-rsp", "Creates a response file with a prepared command");
createRspCommand.AddAlias("rsp");
createRspCommand.SetHandler(CreateResponseFile);

rootCommand.AddCommand(bundleCommand);
rootCommand.AddCommand(createRspCommand);

await rootCommand.InvokeAsync(args);

// Function Definitions

Option<string[]> CreateLanguageOption()
{
    var option= new Option<string[]>("--language",
        description: "Programming languages to include (e.g., csharp, python, java). Use 'all' to include all code files.",
        parseArgument: result =>
        {
            var value = result.Tokens
                .SelectMany(t => t.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Select(v => v.ToLower()) 
                .ToArray();            
            if (value.Length == 0)
            {
                result.ErrorMessage = "At least one language must be specified.";
            }
            return value;
        })
    {
        IsRequired = true
    };
    option.AddAlias("-l");
    return option;
}

HashSet<string> GetSelectedExtensions(string[] languages)
{
    var languageExtensions = new Dictionary<string, string[]>
    {
        { "csharp", new[] { ".cs" } },
        { "dotnet", new[] { ".cs" } },
        { "python", new[] { ".py" } },
        { "java", new[] { ".java" } },
        { "javascript", new[] { ".js" } },
        { "typescript", new[] { ".ts" } },
        { "html", new[] { ".html", ".htm" } },
        { "css", new[] { ".css" } },
        { "cpp", new[] { ".cpp", ".h", ".hpp" } },
        { "c", new[] { ".c", ".h" } },
        { "go", new[] { ".go" } },
        { "php", new[] { ".php" } },
        { "ruby", new[] { ".rb" } },
        { "kotlin", new[] { ".kt", ".kts" } },
        { "swift", new[] { ".swift" } },
        { "perl", new[] { ".pl", ".pm" } },
        { "shell", new[] { ".sh" } },
        { "batch", new[] { ".bat", ".cmd" } },
        { "scala", new[] { ".scala" } },
        { "r", new[] { ".r" } },
        { "sql", new[] { ".sql" } },
        { "react", new[] { ".jsx", ".tsx" } },
        { "angular", new[] { ".ts", ".html", ".css", ".scss" } },
        { "assembler", new[] { ".asm", ".s" } }
    };

    var selected= languages.Contains("all")
        ? languageExtensions.Values.SelectMany(ext => ext).ToHashSet()
        : languages.SelectMany(lang => languageExtensions.GetValueOrDefault(lang, Array.Empty<string>())).ToHashSet();

    var invalidLanguages = languages.Where(lang => !languageExtensions.ContainsKey(lang)).ToList();
    if (invalidLanguages.Any())
    {
        throw new Exception($"Invalid languages specified: {string.Join(", ", invalidLanguages)}");
    }
    return selected;
}

HashSet<string> LoadIgnoredPatterns()
{
    var gitignorePath = Path.Combine(Directory.GetCurrentDirectory(), ".gitignore");
    return File.Exists(gitignorePath)
        ? File.ReadAllLines(gitignorePath)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
        : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "debug", "release", "node_modules", ".git", ".vs" };
}

List<string> GetMatchingFiles(HashSet<string> selectedExtensions, HashSet<string> ignoredPatterns)
{
    return Directory.GetFiles(Directory.GetCurrentDirectory(), "*", SearchOption.AllDirectories)
    .Where(file =>
    {
        var directory = Path.GetDirectoryName(file);
        if (directory != null && ignoredPatterns.Any(dir => directory.Contains(dir, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        return selectedExtensions.Contains(Path.GetExtension(file).ToLower());
    })
    .ToList();
}

List<string> SortFiles(List<string> files, string sort)
{
    return sort.ToLower() switch
    {
        "type" => files.OrderBy(file => Path.GetExtension(file).ToLower()).ThenBy(file => Path.GetFileName(file)).ToList(),
        _ => files.OrderBy(file => Path.GetFileName(file)).ToList(),
    };
}

void WriteToBundleFile(FileInfo output, List<string> files, bool note, string author, bool removeEmptyLines)
{
    using var writer = new StreamWriter(output.FullName);
    if (!string.IsNullOrEmpty(author))
    {
        writer.WriteLine($"# Author: {author}");
    }

    foreach (var file in files)
    {
        if (note)
        {
            writer.WriteLine($"# Source: {Path.GetRelativePath(Directory.GetCurrentDirectory(), file)}");
        }

        var content = File.ReadAllLines(file);
        if (removeEmptyLines)
        {
            content = content.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        }

        foreach (var line in content)
        {
            writer.WriteLine(line);
        }
        writer.WriteLine(); // Add a blank line between files
    }
    Console.WriteLine($"Files have been successfully bundled into {output.FullName}");
}

void CreateResponseFile()
{
    var sb = new StringBuilder("bundle ");

    // Collect user input for the `bundle` command

    // Validate output file path
    string output;

    while (true)
    {
        Console.Write("Enter the output file path: ");
        output = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(output))
        {
            sb.Append($"--output {output} ");
            break;
        }
        Console.WriteLine("Output file path cannot be empty. Please try again.");
    }


    // Validate languages
    string languages;
    while (true)
    {
        Console.Write("Enter the languages to include (e.g., csharp, python, java, javascript, typescript, html, css, cpp or 'all'): ");
        languages = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(languages))
        {
            sb.Append($"--language {languages} ");
            break;
        }
        Console.WriteLine("Language is required. Please provide valid languages.");
    }

    Console.Write("Enter the sort order (type or name, default is name): ");
    var sortOrder = Console.ReadLine();
    if (!string.IsNullOrWhiteSpace(sortOrder))
    {
        sb.Append($"--sort {sortOrder} ");
    }

    Console.Write("Include file sources as comments? (y/n): ");
    var note = Console.ReadLine();
    if (!string.IsNullOrWhiteSpace(note) && note.ToLower() == "y")
    {
        sb.Append("--note ");
    }

    Console.Write("Remove empty lines from files? (y/n): ");
    var removeEmpty = Console.ReadLine();
    if (!string.IsNullOrWhiteSpace(removeEmpty) && removeEmpty.ToLower() == "y")
    {
        sb.Append("--remove-empty-lines ");
    }

    Console.Write("Enter the author name to include in the bundle: ");
    var author = Console.ReadLine();
    if (!string.IsNullOrWhiteSpace(author))
    {
        sb.Append($"--author \"{author}\" ");
    }

    // Validate response file path
    string responseFileName;
    while (true)
    {
        Console.Write("Enter response file name: ");
        responseFileName = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(responseFileName))
        {
            // Add rsp extension if missing
            if (!Path.HasExtension(responseFileName))
            {
                responseFileName = Path.ChangeExtension(responseFileName, ".rsp");
            }

            // Validate file extension
            if (Path.GetExtension(responseFileName)?.ToLower() != ".rsp")
            {
                Console.WriteLine("Invalid file extension. Only '.rsp' is allowed. Please try again.");
                continue;
            }

            // If all checks pass, break the loop
            break;

        }
        else
        {
            Console.WriteLine("Response file path cannot be empty. Please provide a valid path.");
        }
    }


    try
    {
        using (var writer = new StreamWriter(responseFileName))
        {
            writer.Write(sb.ToString().Trim());
        }

        Console.WriteLine($"Response file created successfully at: {responseFileName}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error creating response file: {ex.Message}");
    }

}
