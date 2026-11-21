using FluentAssertions;
using PharmaX.Persistence.Security;

namespace PharmaX.Tests.Unit.Persistence;

public sealed class SystemClockTests
{
    [Fact]
    public void A_correction_moves_now_to_the_servers_time()
    {
        var clock = new SystemClock();
        clock.Correct(TimeSpan.FromMinutes(1));

        (clock.UtcNow - DateTime.UtcNow).TotalSeconds.Should().BeInRange(59, 61);
    }
}
