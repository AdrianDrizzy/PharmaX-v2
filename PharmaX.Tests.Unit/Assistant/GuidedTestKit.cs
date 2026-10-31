using NSubstitute;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Assistant.Guided;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Security;

namespace PharmaX.Tests.Unit.Assistant;

/// <summary>What the guided-flow tests share.</summary>
internal static class GuidedTestKit
{
    public static readonly DateOnly Today = new(2026, 10, 25);

    public static IClock Clock()
    {
        var clock = Substitute.For<IClock>();
        clock.Today.Returns(Today);
        return clock;
    }

    /// <remarks>
    /// Granted is the enum's default, so a bare substitute grants
    /// everything: only the codes named here are granted.
    /// </remarks>
    public static IPermissionService Granting(params string[] codes)
    {
        var granted = new HashSet<string>(codes, StringComparer.Ordinal);
        var permissions = Substitute.For<IPermissionService>();
        permissions.Check(Arg.Any<string>())
            .Returns(ci => granted.Contains((string)ci[0]) ? PermissionOutcome.Granted : PermissionOutcome.Denied);
        return permissions;
    }

    public static ProductListItemDto Listed(Guid id, string code, string name, decimal price, decimal onHand,
                                            int schedule = 0, string? pack = null, decimal? units = null) =>
        new(id, code, name, price, onHand, schedule, false, true, false, pack, units);

    public static ProductDto Detail(Guid id, string code, string name, decimal price, decimal cost, decimal onHand,
                                    decimal reorder = 0m, bool batched = false, int schedule = 0,
                                    string? pack = null) =>
        new(id, code, name, null, price, cost, 15m, "A", schedule, batched, true, reorder, onHand, pack,
            Array.Empty<string>(), null, null, null);

    public static async Task<string> SayAsync(GuidedFlow flow, params string[] answers)
    {
        var last = "";
        foreach (var answer in answers) last = await flow.ReplyAsync(answer);
        return last;
    }

    /// The keys asked, one after each answer, starting with the first.
    public static async Task<List<string?>> KeysAsync(GuidedFlow flow, params string[] answers)
    {
        await flow.StartAsync();
        var asked = new List<string?> { flow.Waiting };

        foreach (var answer in answers)
        {
            await flow.ReplyAsync(answer);
            asked.Add(flow.Waiting);
        }

        return asked;
    }

    /// A prompt hook that answers every authorisation with one person, or nobody.
    public sealed class Prompts : IGuidedPrompts
    {
        private readonly Guid? _who;

        public Prompts(Guid? who) => _who = who;

        public List<string> Asked { get; } = new();

        public Task<Guid?> AuthoriseAsync(string permissionCode, string reason, CancellationToken ct = default)
        {
            Asked.Add(permissionCode);
            return Task.FromResult(_who);
        }
    }
}
