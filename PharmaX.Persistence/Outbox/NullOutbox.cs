using PharmaX.Application.Abstractions;

namespace PharmaX.Persistence.Outbox;

/// <summary>
/// The outbox for a pharmacy running on one shared database.
/// </summary>
/// <remarks>
/// An outbox exists so that work done while the host is unreachable can
/// be sent on later. On a shared database the till is writing into the
/// pharmacy's one database already, so there is nowhere for an entry to
/// travel to and nothing that would ever drain it. Writing them anyway
/// would add a row per transaction to a table that only grows.
/// </remarks>
public sealed class NullOutbox : IOutbox
{
    public void Append(string entityType, Guid entityId, string payloadJson)
    {
    }
}
