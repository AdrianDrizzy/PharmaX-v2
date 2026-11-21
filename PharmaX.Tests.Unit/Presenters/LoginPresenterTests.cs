using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Desktop.Features.Login;

namespace PharmaX.Tests.Unit.Presenters;

public sealed class LoginPresenterTests
{
    private readonly IAuthenticationService _auth = Substitute.For<IAuthenticationService>();
    private readonly IServerClock _server = Substitute.For<IServerClock>();
    private readonly IClockCorrection _correction = Substitute.For<IClockCorrection>();
    private readonly ILoginView _view = Substitute.For<ILoginView>();

    public LoginPresenterTests()
    {
        _view.Username.Returns("leo");
        _view.Password.Returns("pw");
        _auth.AuthenticateAsync("leo", "pw", Arg.Any<CancellationToken>())
            .Returns(Result<AuthenticatedUserDto>.Ok(new AuthenticatedUserDto(
                Guid.NewGuid(), "leo", "Leo", Array.Empty<string>(), false)));
    }

    private void SignIn()
    {
        new LoginPresenter(_auth, _server, _correction).Attach(_view);
        _view.LoginRequested += Raise.EventWith(this, EventArgs.Empty);
    }

    [Fact]
    public void A_clock_hours_out_stops_sign_in_before_the_password_is_checked()
    {
        _server.ReadAsync(Arg.Any<CancellationToken>()).Returns(new ServerTime(
            DateTime.UtcNow.AddHours(10), TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow)));

        SignIn();

        _view.Received().ShowError(Arg.Is<string>(s => s.Contains("clock", StringComparison.Ordinal)));
        _auth.DidNotReceive().AuthenticateAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        _view.DidNotReceive().CloseView(true);
    }

    [Fact]
    public void An_unreachable_server_lets_sign_in_continue_uncorrected()
    {
        _server.ReadAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("down"));

        SignIn();

        _view.Received().CloseView(true);
        _correction.DidNotReceive().Correct(Arg.Any<TimeSpan>());
    }

    [Fact]
    public void A_good_clock_is_corrected_to_the_server()
    {
        _server.ReadAsync(Arg.Any<CancellationToken>()).Returns(new ServerTime(
            DateTime.UtcNow.AddSeconds(30), TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow)));

        SignIn();

        _correction.Received(1).Correct(Arg.Is<TimeSpan>(t => t.TotalSeconds > 25 && t.TotalSeconds < 35));
        _view.Received().CloseView(true);
    }
}
