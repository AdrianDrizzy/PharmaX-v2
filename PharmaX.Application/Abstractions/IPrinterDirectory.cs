namespace PharmaX.Application.Abstractions;

/// <summary>
/// A printer plugged into one of the pharmacy's computers, as that
/// computer reported it.
/// </summary>
/// <param name="ShareName">
/// The name Windows shares it under, or null where it is not shared - in
/// which case no other computer can print to it yet.
/// </param>
public sealed record NetworkPrinter(string Computer, string Name,
    string? ShareName, DateTime SeenAtUtc = default)
{
    public bool IsShared => !string.IsNullOrWhiteSpace(ShareName);

    /// The name another computer prints to it by, or null where it cannot.
    public string? Path => IsShared ? $@"\\{Computer}\{ShareName}" : null;
}

/// <summary>
/// Every computer's printers, so that any one of them - the main computer
/// above all - can list the printers on the others.
/// </summary>
/// <remarks>
/// Kept in the shared database because that is the one thing every
/// PharmaX computer already reaches. Windows can no longer be asked for
/// "every printer on the network" - the browser service that answered
/// went with SMB1 - so each computer says what it has instead.
/// </remarks>
public interface IPrinterDirectory
{
    /// Replaces what is known about one computer's printers.
    Task PublishAsync(string computer, IReadOnlyList<NetworkPrinter> printers,
                      CancellationToken ct = default);

    /// Every computer's printers, as each last reported them.
    Task<IReadOnlyList<NetworkPrinter>> ListAsync(CancellationToken ct = default);
}
