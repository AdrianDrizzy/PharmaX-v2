using System.Text.RegularExpressions;
using FluentAssertions;

namespace PharmaX.Tests.Unit.Architecture;

/// <summary>
/// Nothing on the till's own thread waits for something asynchronous.
/// </summary>
/// <remarks>
/// This froze a live till. The receipt printer read the workstation's
/// chosen printer with GetAwaiter().GetResult(), and a cashier pressing
/// Pay ran that on the user-interface thread. The database query's
/// continuation is posted back to that same thread, which was blocked
/// waiting for the query to finish - so the sale committed, the
/// hourglass came up, and the window never redrew again. The cash
/// drawer and the label printer had the identical line.
///
/// Nothing about it fails to compile, nothing throws, and it only bites
/// when the query is slow enough to actually go asynchronous - which on
/// a developer's machine against a warm local database it often is not.
/// Reading the source is crude, but a deadlock that only appears on a
/// shop counter is worth catching at the moment somebody types it.
/// </remarks>
public class NoBlockingOnAsyncTests
{
    /// <summary>
    /// Where blocking is legitimate: start-up runs before the message
    /// loop exists, so there is no user-interface thread to deadlock and
    /// nothing else to do but wait.
    /// </summary>
    private static readonly string[] Allowed = { "Program.cs" };

    private static readonly string[] Projects =
    {
        "PharmaX.Adapters", "PharmaX.Desktop"
    };

    [Theory]
    [InlineData(@"\.GetAwaiter\(\)\s*\.?\s*GetResult\(\)")]
    [InlineData(@"\.Wait\(\s*\)")]
    public void Nothing_in_the_user_interface_blocks_on_a_task(string pattern)
    {
        var root = SolutionRoot();

        if (root is null) return;   // running from somewhere unexpected

        var blocking = new Regex(pattern, RegexOptions.Compiled);

        // A source scan that reads nothing, or a pattern that matches
        // nothing, passes forever while testing nothing.
        var files = SourceFiles(root).ToList();

        files.Should().NotBeEmpty("the scan has to find the source");

        blocking.IsMatch("            x.SomethingAsync().GetAwaiter()"
                         + ".GetResult();  x.Wait();")
                .Should().BeTrue("the pattern has to match what it hunts");

        var offenders = new List<string>();

        foreach (var file in files)
        {
            if (Allowed.Contains(Path.GetFileName(file),
                                 StringComparer.Ordinal))
                continue;

            var text = File.ReadAllText(file);

            foreach (var line in text.Split('\n')
                         .Select((content, number) => (content, number))
                         .Where(l => blocking.IsMatch(l.content)))
            {
                offenders.Add(
                    $"{Path.GetFileName(file)}:{line.number + 1} "
                    + line.content.Trim());
            }
        }

        offenders.Should().BeEmpty(
            "waiting for a task from the thread that has to run its "
            + "continuation is a deadlock, and the screen it freezes is "
            + "a till with a customer in front of it");
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        Projects
            .Select(project => Path.Combine(root, project))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(
                directory, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                                    StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                                       StringComparison.Ordinal));

    private static string? SolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName,
                                            "PharmaX.sln")))
            directory = directory.Parent;

        return directory?.FullName;
    }
}
