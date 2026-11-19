using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PharmaX.Application.Licensing;

/// Each plan's value is its length in months; Lifetime never ends.
public enum MembershipPlan : byte { Monthly = 1, Quarterly = 3, Yearly = 12, Lifetime = 255 }

public sealed record LicencePayload(MembershipPlan Plan, Guid PharmacyId,
                                    DateOnly ValidUntil, DateOnly IssuedOn);

/// <summary>
/// The membership activation code: a small payload signed with the
/// vendor's private key. PharmaX holds only the public key, so it can
/// check a code but never make one - not even taken apart.
/// </summary>
public static class LicenceCode
{
    /// <summary>The vendor's public key (SubjectPublicKeyInfo, Base64).</summary>
    public const string PublicKey = "";

    private const string Prefix = "PHX-";
    private const byte Version = 1;
    private const int PayloadLength = 26;
    private const int SignatureLength = 64;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private static readonly DateOnly Epoch = new(2000, 1, 1);

    private static readonly Lazy<ECDsa?> BuiltIn = new(() =>
    {
        if (PublicKey.Length == 0) return null;
        var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);
        return key;
    });

    public static string Issue(LicencePayload payload, ECDsa privateKey)
    {
        var data = Encode(payload);
        var signature = privateKey.SignData(data, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        var text = ToBase32([.. data, .. signature]);
        return Prefix + string.Join('-', text.Chunk(5).Select(c => new string(c)));
    }

    public static LicencePayload? Verify(string code) =>
        BuiltIn.Value is { } key ? Verify(code, key) : null;

    public static LicencePayload? Verify(string code, ECDsa publicKey)
    {
        // First, not last: a group inside the code can itself end in "PHX".
        var text = (code ?? "").ToUpperInvariant();
        var start = text.IndexOf(Prefix, StringComparison.Ordinal);
        if (start < 0) return null;

        // Whatever the paste brought along - the message around it, dashes,
        // invisible spaces - is not in the alphabet, so it drops out here.
        var bytes = FromBase32(new string(text[(start + Prefix.Length)..]
            .Where(c => Alphabet.Contains(c, StringComparison.Ordinal)).ToArray()));
        if (bytes is null || bytes.Length != PayloadLength + SignatureLength) return null;

        var data = bytes.AsSpan(0, PayloadLength);
        var signature = bytes.AsSpan(PayloadLength);

        try
        {
            if (!publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                return null;
        }
        catch (CryptographicException)
        {
            return null;
        }

        return Decode(data);
    }

    /// <summary>
    /// The end date a new code should carry: renewing early carries on from
    /// the current end, a lapsed membership starts again from today.
    /// </summary>
    public static DateOnly NextValidUntil(DateOnly today, DateOnly? currentEnd,
                                          MembershipPlan plan)
    {
        var from = currentEnd is { } end && end > today ? end : today;

        // Lifetime - or any code for a pharmacy that already has it - runs to
        // the last date there is.
        return plan == MembershipPlan.Lifetime || from == DateOnly.MaxValue
            ? DateOnly.MaxValue
            : from.AddMonths((int)plan);
    }

    public static string FormatPharmacyId(Guid id) =>
        string.Join('-', id.ToString("N").ToUpperInvariant().Chunk(4)
                           .Select(c => new string(c)));

    public static Guid? ParsePharmacyId(string text)
    {
        var hex = new string((text ?? "").Where(char.IsAsciiHexDigit).ToArray());
        return hex.Length == 32 && Guid.TryParseExact(hex, "N", out var id) ? id : null;
    }

    private static byte[] Encode(LicencePayload p)
    {
        var data = new byte[PayloadLength];
        data[0] = Version;
        data[1] = (byte)p.Plan;
        p.PharmacyId.TryWriteBytes(data.AsSpan(2, 16));
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(18), p.ValidUntil.DayNumber - Epoch.DayNumber);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(22), p.IssuedOn.DayNumber - Epoch.DayNumber);
        return data;
    }

    private static LicencePayload? Decode(ReadOnlySpan<byte> data)
    {
        if (data[0] != Version) return null;
        var plan = (MembershipPlan)data[1];
        if (!Enum.IsDefined(plan)) return null;

        var until = BinaryPrimitives.ReadInt32BigEndian(data[18..]);
        var issued = BinaryPrimitives.ReadInt32BigEndian(data[22..]);
        if (until < 0 || issued < 0) return null;

        return new LicencePayload(plan, new Guid(data.Slice(2, 16)),
            DateOnly.FromDayNumber(Epoch.DayNumber + until),
            DateOnly.FromDayNumber(Epoch.DayNumber + issued));
    }

    private static string ToBase32(byte[] bytes)
    {
        var sb = new StringBuilder((bytes.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static byte[]? FromBase32(string text)
    {
        var bytes = new List<byte>(text.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in text)
        {
            var value = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0) return null;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return bytes.ToArray();
    }
}
