using System.CommandLine;

namespace tsbindgen.Cli;

/// <summary>
/// Entry point for tsbindgen CLI.
/// Uses new two-phase pipeline: generate command only.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Generate TypeScript declarations from .NET assemblies");

        // Add the generate command
        var generateCommand = GenerateCommand.Create();
        rootCommand.AddCommand(generateCommand);

        return await rootCommand.InvokeAsync(args);
    }
}
