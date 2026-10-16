using FluentAssertions;
using PharmaX.Application.Offline;
using PharmaX.Desktop.Features.Login;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// The banner across the top of a screen that says the host cannot be
/// reached is read whole, on a screen laid out by position as much as on
/// one laid out by docking.
/// </summary>
public class OfflineBannerTests
{
    private const string Offline =
        "Working locally - transactions will sync when the host returns";

    /// <remarks>
    /// The sign-in window places its layout at the top left, and it sat
    /// over the banner: all that showed was "urns" in a grey box.
    /// </remarks>
    [Fact]
    public void The_sign_in_window_moves_down_to_show_the_banner()
    {
        var monitor = new Monitor();
        using var form = Open(monitor);

        var banner = Banner(form);
        var layout = form.Controls["layRoot"]!;

        banner.Visible.Should().BeTrue();
        banner.Text.Should().Be(Offline);
        layout.Top.Should().BeGreaterThanOrEqualTo(banner.Bottom,
            "nothing sits over the banner");
        form.ClientSize.Height.Should().BeGreaterThanOrEqualTo(layout.Bottom,
            "and nothing falls off the bottom for it");
    }

    [Fact]
    public void The_window_closes_up_again_once_the_host_is_back()
    {
        var monitor = new Monitor();
        using var form = Open(monitor);
        var layout = form.Controls["layRoot"]!;
        var offline = form.ClientSize.Height;

        monitor.Become(ConnectivityState.Connected);
        System.Windows.Forms.Application.DoEvents();

        Banner(form).Visible.Should().BeFalse();
        layout.Top.Should().Be(0);
        form.ClientSize.Height.Should().BeLessThan(offline);
    }

    private static frmLogin Open(Monitor monitor)
    {
        var form = new frmLogin
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-3000, -3000),
            ShowInTaskbar = false
        };

        form.UseConnectivity(monitor);
        form.Show();
        return form;
    }

    private static Label Banner(Form form) =>
        form.Controls.OfType<Label>().Single(l => l.Dock == DockStyle.Top);

    private sealed class Monitor : IConnectivityMonitor
    {
        public ConnectivityState State { get; private set; } =
            ConnectivityState.LocalOnly;

        public DateTime? LastContactUtc => null;

        public event EventHandler<ConnectivityState>? StateChanged;

        public Task<ConnectivityState> ProbeAsync(CancellationToken ct = default) =>
            Task.FromResult(State);

        public string DescribeState(ConnectivityState state) =>
            state == ConnectivityState.Connected ? "" : Offline;

        public void Become(ConnectivityState state)
        {
            State = state;
            StateChanged?.Invoke(this, state);
        }
    }
}
