using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using Spectre.Console;

namespace LLama.Examples.Examples;

public class ParamsFitExample
{
    public static async Task Run()
    {
        var modelPath = UserSettings.GetModelPath();

        var mode = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Choose a [green]fitting mode[/]:")
                .AddChoices(
                    "Maximize context (at least 32k)",
                    "Fixed 32k context"));

        var marginGiB = AnsiConsole.Prompt(
            new TextPrompt<int>("Extra margin per device in [green]GiB[/] (0 = default 1 GiB):")
                .DefaultValue(0));

        var marginBytes = marginGiB > 0
            ? (long)marginGiB * 1024 * 1024 * 1024
            : LLamaParamsFit.DefaultMarginBytes;

        var parameters = new ModelParams(modelPath);

        LLamaParamsFitStatus status;
        if (mode.StartsWith("Maximize"))
        {
            // Let fit determine the best context size, starting from the model's max.
            // It will reduce context if needed but won't go below 32768.
            parameters.ContextSize = null;
            status = LLamaParamsFit.Fit(parameters, marginBytes: marginBytes, minContextSize: 32768);
        }
        else
        {
            // Fix context at exactly 32768. Fit will only adjust GPU layers
            // and tensor splits to accommodate this fixed context size.
            parameters.ContextSize = 32768;
            status = LLamaParamsFit.Fit(parameters, marginBytes: marginBytes);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[yellow]Fit status:[/] [cyan]{status}[/]");
        AnsiConsole.MarkupLine("[yellow]After fit:[/]");
        AnsiConsole.MarkupLine($"  ContextSize:    [cyan]{parameters.ContextSize?.ToString() ?? "null (unchanged)"}[/]");
        AnsiConsole.MarkupLine($"  GpuLayerCount:  [cyan]{parameters.GpuLayerCount}[/]");

        if (status != LLamaParamsFitStatus.Success)
        {
            AnsiConsole.MarkupLine("[red]Fit did not succeed. Cannot load model with these constraints.[/]");
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Loading model with fitted parameters...[/]");
        using var model = await LLamaWeights.LoadFromFileAsync(parameters);
        using var context = model.CreateContext(parameters);

        AnsiConsole.MarkupLine($"[green]Model loaded successfully. Actual context size: {context.ContextSize}[/]");
        AnsiConsole.WriteLine();

        var executor = new InteractiveExecutor(context);
        var inferenceParams = new InferenceParams
        {
            AntiPrompts = new List<string> { "User:" },
            MaxTokens = 256,
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = 0.6f
            }
        };

        AnsiConsole.MarkupLine("[yellow]Chat started. Type your message and press Enter. Type 'exit' to quit.[/]");
        Console.WriteLine();

        var prompt = "User: ";
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write(prompt);
        prompt = Console.ReadLine() ?? "";

        while (prompt != "exit")
        {
            prompt = "User: " + prompt + "\nAssistant: ";
            await foreach (var text in executor.InferAsync(prompt, inferenceParams))
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(text);
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("User: ");
            prompt = Console.ReadLine() ?? "";
        }
    }
}
