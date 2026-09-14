namespace PharmaX.Application.Offline;

/// <summary>
/// Three states rather than a bool, because "the host answered slowly" and
/// "the host did not answer" call for different messages to the operator
/// and different behaviour from the reconciler.
/// </summary>
public enum ConnectivityState
{
    Connected = 0,
    Degraded = 1,

    /// Named for what it is rather than for what is missing: the
    /// workstation is working, against its own database (NET-002).
    LocalOnly = 2
}

public interface IConnectivityMonitor
{
    ConnectivityState State { get; }
    DateTime? LastContactUtc { get; }

    event EventHandler<ConnectivityState>? StateChanged;

    /// Probes the host now rather than waiting for the next poll. Returns
    /// the state it found.
    Task<ConnectivityState> ProbeAsync(CancellationToken ct = default);

    /// <summary>
    /// What a lost connection means here, in words a screen can show.
    /// </summary>
    /// <remarks>
    /// It means two different things. On a standalone workstation the
    /// till carries on and syncs later, which is reassuring and true.
    /// On a shared database the till cannot carry on at all, and
    /// telling the operator it is "working locally" would be a lie at
    /// the moment they most need the truth.
    /// </remarks>
    string DescribeState(ConnectivityState state);
}
