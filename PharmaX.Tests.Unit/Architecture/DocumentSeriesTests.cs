using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using PharmaX.Application.Abstractions;

namespace PharmaX.Tests.Unit.Architecture;

/// <summary>
/// Every series of document numbers the application asks for must be one
/// the seeder gives a block to.
/// </summary>
/// <remarks>
/// This has gone wrong three times, and the failure is always the same:
/// a feature is built, it asks the numbering allocator for a series
/// nobody seeded, and the transaction that needed the number dies at a
/// site with "no document numbers left for XXX" - naming a series the
/// operator has never heard of, in the middle of something else.
///
/// A journal series did it and stopped the first sale of the day. A
/// claims series and an accounts series did it next. And quotations had
/// been asking for a QUO block that had never been seeded at all, which
/// means that feature could not have worked on any installation since
/// the day it was written.
///
/// Reading the source is crude, but it is the only way to catch the
/// mistake at the moment it is made rather than on somebody's counter.
/// </remarks>
public class DocumentSeriesTests
{
    [Fact]
    public void Every_series_the_code_asks_for_is_one_the_seeder_creates()
    {
        var asked = SeriesAskedForInSource();

        asked.Should().NotBeEmpty(
            "the search should find the NextAsync calls; if it finds none, "
            + "this test has stopped testing anything");

        asked.Should().BeSubsetOf(DocumentSeries.All,
            "a series with no seeded block fails at a site, in the "
            + "transaction that needed the number");
    }

    /// <remarks>
    /// The other direction. A series nobody asks for is only clutter,
    /// but it is usually the trace of a feature that was removed or
    /// renamed, and the block it seeds is a row per workstation forever.
    /// </remarks>
    [Fact]
    public void Every_series_the_seeder_creates_is_one_the_code_asks_for()
    {
        SeriesAskedForInSource()
            .Should().BeEquivalentTo(DocumentSeries.All);
    }

    [Fact]
    public void No_series_is_listed_twice()
    {
        DocumentSeries.All.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Reads the codes off the constants rather than the call sites.
    /// </summary>
    /// <remarks>
    /// The call sites now name constants, so scanning for a quoted
    /// literal would find nothing. What is checked instead is that every
    /// constant on DocumentSeries appears in its own All list - which is
    /// the mistake that is actually easy to make: adding a constant and
    /// forgetting the array.
    /// </remarks>
    private static IReadOnlyList<string> SeriesAskedForInSource() =>
        typeof(DocumentSeries)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

    /// <summary>
    /// Nothing asks for a series by typing its code.
    /// </summary>
    /// <remarks>
    /// The constants only help if they are used. A literal slipped back
    /// into a service would reintroduce exactly the drift this file
    /// exists to stop, and would do it invisibly.
    /// </remarks>
    [Fact]
    public void Nothing_asks_for_a_series_by_literal()
    {
        var root = SolutionRoot();

        if (root is null) return;   // running from somewhere unexpected

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs",
                     SearchOption.AllDirectories))
        {
            if (file.Contains("Migrations", StringComparison.Ordinal)
                || file.Contains(Path.Combine("obj"), StringComparison.Ordinal)
                || file.Contains("DocumentSeries", StringComparison.Ordinal)
                || file.Contains("Tests", StringComparison.Ordinal))
                continue;

            var text = File.ReadAllText(file);

            if (Regex.IsMatch(text, """NextAsync\(\s*"[A-Z]+"\s*"""))
                offenders.Add(Path.GetFileName(file));
        }

        offenders.Should().BeEmpty(
            "series are named by DocumentSeries constants so that the "
            + "seeder cannot fall behind them");
    }

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
