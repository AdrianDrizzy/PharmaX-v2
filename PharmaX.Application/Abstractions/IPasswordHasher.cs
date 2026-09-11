namespace PharmaX.Application.Abstractions;

/// <summary>
/// Hashing lives behind an interface so that the algorithm can be replaced
/// without touching authentication, and so that a test can substitute
/// something fast for something deliberately slow.
/// </summary>
public interface IPasswordHasher
{
    /// Returns the full encoded hash: algorithm, parameters and salt
    /// included, so that a stored credential is self-describing.
    string Hash(string password);

    bool Verify(string password, string encodedHash);

    /// True where the stored hash was produced with parameters weaker than
    /// the current ones, so a good login can quietly re-hash and upgrade it.
    bool NeedsRehash(string encodedHash);
}
