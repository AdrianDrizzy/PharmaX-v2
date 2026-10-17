using System.Globalization;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Offline;
using PharmaX.Application.Reporting;
using PharmaX.Application.Repositories;
using PharmaX.Application.Security;

namespace PharmaX.Reporting.Engine;

public sealed class ReportRunner : IReportRunner
{
    private readonly IPresetCatalogue _presets;
    private readonly IEnumerable<IReportDefinition> _definitions;
    private readonly IEnumerable<IReportExporter> _exporters;
    private readonly IPermissionService _permissions;
    private readonly IConnectivityMonitor _connectivity;
    private readonly IUserRepository _users;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public ReportRunner(IPresetCatalogue presets,
        IEnumerable<IReportDefinition> definitions,
        IEnumerable<IReportExporter> exporters,
        IPermissionService permissions, IConnectivityMonitor connectivity,
        IUserRepository users, ICurrentUser user, IClock clock,
        IAuditWriter audit, IUnitOfWork uow)
    {
        _presets = presets;
        _definitions = definitions;
        _exporters = exporters;
        _permissions = permissions;
        _connectivity = connectivity;
        _users = users;
        _user = user;
        _clock = clock;
        _audit = audit;
        _uow = uow;
    }

    public async Task<ReportResult> RunAsync(ReportRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var preset = _presets.Resolve(request.PresetKey);

        // Permission is checked against the preset, not the definition:
        // the same query is operational under one name and financial
        // under another, and the name is what the manager granted.
        _permissions.Demand(preset.PermissionCode);

        var definition = _definitions.FirstOrDefault(d =>
            string.Equals(d.Key, preset.DefinitionKey, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Preset '{preset.Key}' names definition "
                + $"'{preset.DefinitionKey}', which is not registered.");

        var resolved = new ResolvedRequest(
            preset, request.From, request.To, request.BranchCode,
            Merge(preset.FixedSlicers, request.Slicers),
            BuildProvenance(request, preset),
            await OperatorNamesAsync(ct).ConfigureAwait(false));

        var result = await definition.RunAsync(resolved, ct).ConfigureAwait(false);

        await RecordAsync(preset, ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Who asked for which report, and when. The home screen's Reports
    /// tile says when one was last asked for, and nothing recorded it.
    /// </summary>
    /// <remarks>
    /// A report already run is not taken away because its line in the
    /// trail could not be written.
    /// </remarks>
    private async Task RecordAsync(ReportPreset preset, CancellationToken ct)
    {
        try
        {
            _audit.Applied(ReportRuns.Action, ReportRuns.EntityType, Guid.Empty,
                           preset.DisplayName);
            await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // The database could not be reached.
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // As above.
        }
    }

    public async Task<string> ExportAsync(ReportRequest request,
        ExportFormat format, CancellationToken ct = default)
    {
        var result = await RunAsync(request, ct).ConfigureAwait(false);

        var exporter = _exporters.FirstOrDefault(e => e.Format == format)
            ?? throw new InvalidOperationException(
                $"No exporter registered for {format}.");

        var directory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments),
            "PharmaX", "Reports");

        Directory.CreateDirectory(directory);

        return exporter.Write(result, directory);
    }

    /// <summary>
    /// The preset's fixed slicers win over the user's. A preset that
    /// fixes ratedOnly to true is the definition of that report name, and
    /// a screen must not be able to unset it.
    /// </summary>
    private static Dictionary<string, string?> Merge(
        IReadOnlyDictionary<string, string?> fixedSlicers,
        IReadOnlyDictionary<string, string?> userSlicers)
    {
        var merged = new Dictionary<string, string?>(userSlicers,
                                                     StringComparer.Ordinal);

        foreach (var (key, value) in fixedSlicers)
            merged[key] = value;

        return merged;
    }

    private ReportProvenance BuildProvenance(ReportRequest request,
                                             ReportPreset preset)
    {
        var summary = string.Format(CultureInfo.CurrentCulture,
            "{0} | Branch {1} | {2:dd/MM/yyyy} to {3:dd/MM/yyyy}{4}",
            preset.DisplayName, request.BranchCode, request.From,
            request.To, Describe(request.Slicers));

        // A report run while the host is unreachable is a report of what
        // this workstation knows, and it says so on the page (RPT-012).
        var currency = _connectivity.State == ConnectivityState.Connected
            ? "Data current to the host"
            : "Local data only - this workstation has not reached the host";

        return new ReportProvenance(_user.Username, _clock.UtcNow.ToLocalTime(),
                                    summary, currency);
    }

    private static string Describe(
        IReadOnlyDictionary<string, string?> slicers)
    {
        var applied = slicers
            .Where(s => !string.IsNullOrWhiteSpace(s.Value))
            .Select(s => $"{s.Key}={s.Value}")
            .ToList();

        return applied.Count == 0
            ? " | no filters"
            : " | " + string.Join(", ", applied);
    }

    private async Task<Func<Guid, string>> OperatorNamesAsync(
        CancellationToken ct)
    {
        var cache = new Dictionary<Guid, string>();

        return id =>
        {
            if (cache.TryGetValue(id, out var name)) return name;

            // Blocking on a lookup inside a projection is acceptable here
            // and nowhere else: it happens once per distinct operator on
            // the report, not once per row.
            var user = _users.GetAsync(id, ct).GetAwaiter().GetResult();
            cache[id] = user?.DisplayName ?? id.ToString();
            return cache[id];
        };
    }
}
