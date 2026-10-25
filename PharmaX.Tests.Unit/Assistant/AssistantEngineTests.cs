using System.Runtime.Loader;
using FluentAssertions;
using PharmaX.Assistant;

namespace PharmaX.Tests.Unit.Assistant;

/// <summary>
/// The assistant's engine - LLamaSharp and llama.cpp - is kept out of
/// PharmaX: it runs in a second process, in a load context of its own,
/// with its own newer dependencies.
/// </summary>
public class AssistantEngineTests
{
    /// <summary>
    /// The whole point of the engine's separate process: if LLamaSharp
    /// came back into PharmaX's references, so would its Microsoft.Extensions
    /// and System.Text.Json 10.x, and llama.cpp's crashes with them.
    /// </summary>
    [Fact]
    public void Nothing_in_PharmaX_references_the_inference_engine()
    {
        var pharmax = new[]
        {
            typeof(PharmaX.Desktop.Features.Reports.ReportsPresenter).Assembly,
            typeof(ReportPrompt).Assembly,
            typeof(PharmaX.Application.Reporting.IReportSuggester).Assembly
        };

        pharmax.SelectMany(a => a.GetReferencedAssemblies())
            .Select(a => a.Name)
            .Should().NotContain(new[] { "LLamaSharp", "PharmaX.Assistant.Engine" });

        File.Exists(Path.Combine(AppContext.BaseDirectory, "LLamaSharp.dll"))
            .Should().BeFalse("the engine's packages belong in its own folder, not beside PharmaX");
    }

    [Fact]
    public void The_engine_answers_every_request_and_stays_in_a_context_of_its_own()
    {
        var input = new StringReader(string.Join("\n",
            "{\"op\":\"ask\",\"turn\":\"hello\",\"grammar\":\"root ::= \\\"a\\\"\",\"maxTokens\":5}",
            "{\"op\":\"dance\"}",
            "not json at all",
            ""));
        var output = new StringWriter();

        var code = AssistantHost.RunEngine(input, output, EngineFolder());

        code.Should().Be(0, "the input ended, so the engine finished");

        var replies = output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        replies.Should().HaveCount(3, "every request gets one reply, however bad");
        replies.Should().OnlyContain(r => r.StartsWith("PXA {\"ok\":false", StringComparison.Ordinal));
        replies[0].Should().Contain("has not read its instructions yet");
        replies[1].Should().Contain("no request").And.Contain("dance");

        AssemblyLoadContext.All
            .Single(c => c.Name == "PharmaX assistant engine" && c.Assemblies.Any(a => a.GetName().Name == "LLamaSharp"))
            .Should().NotBeSameAs(AssemblyLoadContext.Default);
        AssemblyLoadContext.Default.Assemblies
            .Should().NotContain(a => a.GetName().Name == "LLamaSharp");
    }

    [Theory]
    [InlineData(new[] { "--assistant-engine" }, true)]
    [InlineData(new[] { "--backup" }, false)]
    [InlineData(new string[0], false)]
    public void Only_the_engine_switch_starts_PharmaX_as_an_engine(string[] args, bool engine)
    {
        AssistantHost.IsRequested(args).Should().Be(engine);
    }

    /// <summary>
    /// Beside the tests where the build shares one output folder; else in
    /// the engine project's own output.
    /// </summary>
    private static string EngineFolder()
    {
        if (AssistantHost.IsInstalled) return AssistantHost.EngineDirectory;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PharmaX.sln")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the tests run from inside the repository");

#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        return Path.Combine(dir!.FullName, "PharmaX.Assistant.Engine", "bin", configuration, "net8.0");
    }
}
