using System.Text.Json;
using FluentAssertions;
using PharmaX.Desktop.Setup;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// What the first-run wizard writes, and the one rule it exists to
/// protect: that no two machines trade under the same identity.
/// </summary>
public class WorkstationProvisioningTests
{
    /// <summary>
    /// The reason this file is separate from the one the installer ships.
    /// </summary>
    /// <remarks>
    /// A WorkstationId is stamped on every document a machine creates,
    /// and the unique index on (WorkstationId, DocumentNumber) means two
    /// machines sharing one would collide on the first receipt they both
    /// issued. Generating it in the profile's own initialiser is what
    /// makes that impossible to get wrong by copying a file.
    /// </remarks>
    [Fact]
    public void Every_profile_gets_an_identity_of_its_own()
    {
        var first = new WorkstationProfile();
        var second = new WorkstationProfile();

        first.WorkstationId.Should().NotBe(second.WorkstationId);
        first.WorkstationId.Should().NotBeEmpty();
    }

    /// <remarks>
    /// Every machine at one site shares a branch, so their rows belong
    /// together on a report.
    /// </remarks>
    [Fact]
    public void Machines_at_one_site_share_a_branch()
    {
        new WorkstationProfile().BranchId.Should()
            .Be(new WorkstationProfile().BranchId);
    }

    [Fact]
    public void A_standalone_machine_has_no_shared_connection()
    {
        var profile = new WorkstationProfile
        {
            Kind = WorkstationKind.Standalone,
            ServerHost = "PHARMACY-PC",
            DatabaseUser = "pharmax"
        };

        profile.IsShared.Should().BeFalse();
        profile.SharedConnection.Should().BeEmpty();
    }

    /// <summary>
    /// The server reaches its own database over the loopback.
    /// </summary>
    /// <remarks>
    /// A small pharmacy is a workgroup with no DNS, and a machine that
    /// cannot resolve its own hostname would otherwise fail to reach a
    /// database sitting on its own disk.
    /// </remarks>
    [Fact]
    public void The_server_talks_to_itself_over_the_loopback()
    {
        var profile = new WorkstationProfile
        {
            Kind = WorkstationKind.Server,
            ServerHost = "PHARMACY-PC",
            DatabaseName = "PharmaX",
            DatabaseUser = "pharmax",
            DatabasePassword = "secret"
        };

        profile.SharedConnection.Should().Contain("Server=127.0.0.1");
        profile.SharedConnection.Should().NotContain("PHARMACY-PC");
    }

    [Fact]
    public void A_till_talks_to_the_machine_it_was_given()
    {
        var profile = new WorkstationProfile
        {
            Kind = WorkstationKind.Till,
            ServerHost = "PHARMACY-PC",
            ServerPort = 3307,
            DatabaseName = "PharmaX",
            DatabaseUser = "pharmax",
            DatabasePassword = "secret"
        };

        profile.SharedConnection.Should().Contain("Server=PHARMACY-PC");
        profile.SharedConnection.Should().Contain("Port=3307");
        profile.SharedConnection.Should().Contain("Database=PharmaX");
    }

    /// <summary>
    /// The file has to bind to PharmaXOptions without any code of its
    /// own, because that is the whole trick: Program.cs adds it as one
    /// more JSON source and everything downstream is unchanged.
    /// </summary>
    [Fact]
    public void The_file_is_written_in_the_shape_of_appsettings()
    {
        var profile = new WorkstationProfile
        {
            Kind = WorkstationKind.Till,
            ServerHost = "PHARMACY-PC",
            DatabaseName = "PharmaX",
            DatabaseUser = "pharmax",
            DatabasePassword = "secret",
            TillNumber = "2",
            TradingName = "Bethrapha Pharmacy",
            Currency = "ZAR",
            ServerVersion = "8.0.42-mysql"
        };

        using var document = JsonDocument.Parse(Render(profile));
        var root = document.RootElement;

        root.GetProperty("Workstation").GetProperty("TillNumber")
            .GetString().Should().Be("2");

        root.GetProperty("Workstation").GetProperty("Role")
            .GetString().Should().Be("Client");

        root.GetProperty("Workstation").GetProperty("WorkstationId")
            .GetString().Should().Be(profile.WorkstationId.ToString());

        root.GetProperty("Pharmacy").GetProperty("Currency")
            .GetString().Should().Be("ZAR");

        root.GetProperty("Database").GetProperty("SharedConnection")
            .GetString().Should().Contain("PHARMACY-PC");

        root.GetProperty("Database").GetProperty("ServerVersion")
            .GetString().Should().Be("8.0.42-mysql");
    }

    /// <remarks>
    /// The database password is the pharmacy's to choose, so whatever it
    /// holds must come back out of the connection string unchanged.
    /// </remarks>
    [Theory]
    [InlineData("pharmacy1")]
    [InlineData("a;b")]
    [InlineData("it's=\"x\"")]
    public void Any_password_survives_the_connection_string(string password)
    {
        var profile = new WorkstationProfile
        {
            Kind = WorkstationKind.Till,
            ServerHost = "PHARMACY-PC",
            DatabasePassword = password
        };

        new MySqlConnector.MySqlConnectionStringBuilder(profile.SharedConnection)
            .Password.Should().Be(password);
    }

    [Fact]
    public void The_login_defaults_to_the_one_the_installer_makes()
    {
        new WorkstationProfile().DatabaseUser.Should().Be("pharmax");
    }

    [Fact]
    public void The_logo_chosen_at_setup_is_written_for_the_pharmacy()
    {
        var profile = new WorkstationProfile { LogoPath = @"C:\ProgramData\PharmaX\branding\logo.png" };

        using var document = JsonDocument.Parse(Render(profile));

        document.RootElement.GetProperty("Pharmacy").GetProperty("LogoPath")
            .GetString().Should().Be(profile.LogoPath);
    }

    /// <remarks>
    /// The server is the host in the sense the rest of PharmaX means it:
    /// the machine that holds the database everything else reads.
    /// </remarks>
    [Fact]
    public void The_main_computer_is_written_as_the_host()
    {
        var profile = new WorkstationProfile { Kind = WorkstationKind.Server };

        using var document = JsonDocument.Parse(Render(profile));

        document.RootElement.GetProperty("Workstation").GetProperty("Role")
            .GetString().Should().Be("Host");
    }

    /// <summary>
    /// The JSON the wizard would write, without writing it.
    /// </summary>
    /// <remarks>
    /// Render rather than Save on purpose: Save writes to ProgramData,
    /// and a test that called it would overwrite the workstation.json of
    /// whatever machine happened to be running the suite.
    /// </remarks>
    private static string Render(WorkstationProfile profile) =>
        WorkstationProvisioning.Render(profile);
}
