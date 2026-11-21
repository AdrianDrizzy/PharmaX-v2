using PharmaX.Application.Dispensing.Dtos;

namespace PharmaX.Application.Dispensing;

/// <summary>
/// Labels that would print EXP:__/__/____. A template label that leaves
/// the expiry off on purpose is not one of them.
/// </summary>
public static class LabelExpiryCheck
{
    public static IReadOnlyList<LabelDto> Missing(IEnumerable<LabelDto> labels) =>
        labels.Where(l => l.Expiry is null && l.Omit?.Expiry != true).ToList();
}
