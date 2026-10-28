namespace PharmaX.Application.Assistant.Guided;

/// <summary>
/// What a guided job needs from the screen it runs on: a second person's
/// own sign-in, asked on the existing authorisation prompt.
/// </summary>
/// <remarks>
/// Never in the conversation. A password typed into the chat would sit in
/// its history for anyone at the counter to read, so the flow asks for the
/// authority and the screen puts up the same sign-in box the till and the
/// dispensary use (SEC-006).
/// </remarks>
public interface IGuidedPrompts
{
    /// <summary>
    /// The person who authorised, having signed in with a user that holds
    /// <paramref name="permissionCode"/>; null where nobody did.
    /// </summary>
    Task<Guid?> AuthoriseAsync(string permissionCode, string reason, CancellationToken ct = default);
}

/// <summary>A screen with no prompt: nothing can be authorised there.</summary>
public sealed class NoPrompts : IGuidedPrompts
{
    public static NoPrompts Instance { get; } = new();

    public Task<Guid?> AuthoriseAsync(string permissionCode, string reason, CancellationToken ct = default) =>
        Task.FromResult<Guid?>(null);
}
