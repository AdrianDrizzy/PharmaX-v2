using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Results;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Application.Security.Dtos;
using PharmaX.Desktop.Features.Sales;
using PharmaX.Desktop.Mvp;
using PharmaX.Domain.Security;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Presenters;

/// <summary>
/// DEF-0011: a screen's operations run one at a time, so its one DbContext
/// is never asked for two things at once - which is what crashed Claims
/// and Tariffs on opening.
/// </summary>
public class OperationQueueTests
{
    public interface ITestView : IView
    {
        event EventHandler? Clicked;
    }

    private sealed class TestPresenter : PresenterBase<ITestView>
    {
        public List<string> Log { get; } = new();
        public TaskCompletionSource FirstLoad { get; } = new();

        protected override void Subscribe() =>
            View.Clicked += (_, _) => Enqueue(ClickAsync);

        /// Holds the screen's first load open until the test releases it,
        /// the way a slow database read would.
        public override Task InitialiseAsync() => GuardAsync(async () =>
        {
            Log.Add("load started");
            await FirstLoad.Task;
            Log.Add("load finished");
        });

        private Task ClickAsync() => GuardAsync(() =>
        {
            Log.Add("click");
            return Task.CompletedTask;
        });

        public Task ThrowingAsync() => GuardAsync(() =>
            throw new InvalidOperationException("boom"));
    }

    [Fact]
    public async Task An_event_raised_during_a_load_waits_for_the_load_to_finish()
    {
        var view = Substitute.For<ITestView>();
        var sut = new TestPresenter();
        sut.Attach(view);

        var loading = sut.InitialiseAsync();

        // The selection event a list raises while it is being filled.
        view.Clicked += Raise.Event();

        sut.Log.Should().Equal("load started");

        sut.FirstLoad.SetResult();
        await loading;
        await Task.Yield();

        sut.Log.Should().Equal("load started", "load finished", "click");
    }

    [Fact]
    public async Task An_unexpected_failure_is_reported_on_the_screen_not_lost()
    {
        var view = Substitute.For<ITestView>();
        var sut = new TestPresenter();
        sut.Attach(view);
        sut.FirstLoad.SetResult();

        await sut.ThrowingAsync();

        view.Received().ShowError(Arg.Is<string>(m =>
            m.Contains("boom", StringComparison.Ordinal)));
    }
}

/// <summary>
/// DEF-0014 and DEF-0030 at the till: hold parks the basket for real, and
/// closing never leaves one behind nobody can reach.
/// </summary>
public class PosHoldTests
{
    private readonly ISaleService _sales = Substitute.For<ISaleService>();
    private readonly IPosView _view = Substitute.For<IPosView>();

    private PosPresenter CreateSut()
    {
        var headers = new ReceiptHeaderFactory(
            new PharmaX.Application.Abstractions.PharmaXOptions(),
            new PharmaX.Application.Abstractions.PharmacyProfile(
                new PharmaX.Application.Abstractions.PharmaXOptions()),
            new FakeCurrentUser(), new FakeClock(),
            new Branding());

        var sut = new PosPresenter(_sales, Substitute.For<ICollectionService>(),
            Substitute.For<IPermissionService>(),
            Substitute.For<IReceiptPrinter>(), Substitute.For<ICashDrawer>(),
            headers);

        sut.Attach(_view);
        return sut;
    }

    private static SaleDto Sale(bool withLine)
    {
        var lines = withLine
            ? new[] { new SaleLineDto(1, Guid.NewGuid(), "Item", 1m, 10m, 0m,
                                      "A", 10m, null, null, false, false) }
            : Array.Empty<SaleLineDto>();

        return new SaleDto(Guid.NewGuid(), "", lines,
            new SaleTotalsDto(10m, 0m, 1.3m, 10m, 0m, 0m));
    }

    [Fact]
    public async Task Hold_parks_the_sale_and_starts_a_new_one()
    {
        var first = Sale(withLine: true);
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(first),
                                   Result<SaleDto>.Ok(Sale(false)));
        _sales.HoldAsync(first.Id).Returns(Result.Ok());

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.HoldRequested += Raise.EventWith(this, EventArgs.Empty);

        await _sales.Received().HoldAsync(first.Id);
        await _sales.Received(2).OpenAsync();
        _view.Received().ShowInfo(Arg.Is<string>(m =>
            m.Contains("F9", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Closing_on_an_empty_sale_abandons_it_quietly()
    {
        var empty = Sale(withLine: false);
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(empty));
        _sales.AbandonAsync(empty.Id, Arg.Any<string>()).Returns(Result.Ok());

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.CloseRequested += Raise.EventWith(this, EventArgs.Empty);

        await _sales.Received().AbandonAsync(empty.Id, Arg.Any<string>());
        _view.Received().CloseView(false);
    }

    [Fact]
    public async Task Closing_with_items_offers_to_hold_them()
    {
        var sale = Sale(withLine: true);
        _sales.OpenAsync().Returns(Result<SaleDto>.Ok(sale));
        _sales.HoldAsync(sale.Id).Returns(Result.Ok());
        _view.Confirm(Arg.Any<string>()).Returns(true);

        var sut = CreateSut();
        await sut.InitialiseAsync();

        _view.CloseRequested += Raise.EventWith(this, EventArgs.Empty);

        await _sales.Received().HoldAsync(sale.Id);
        await _sales.DidNotReceive().AbandonAsync(Arg.Any<Guid>(),
                                                   Arg.Any<string>());
        _view.Received().CloseView(false);
    }
}

/// <summary>
/// A branch manager maintains the staff list, but cannot hand out a role
/// wider than their own.
/// </summary>
public class RoleAssignmentTests
{
    [Fact]
    public async Task A_manager_cannot_make_anyone_a_pharmacist_or_an_administrator()
    {
        var audit = new FakeAuditWriter();
        var manager = new FakeCurrentUser();

        var users = new PharmaX.Application.Security.UserService(
            new InMemoryUserRepository(),
            new PermissionService(manager,
                new FakePermissionRepository(
                    StandardRoleGrants.For(Role.Standard.Manager)),
                audit),
            new FakePasswordHasher(), audit, new FakeUnitOfWork(), manager,
            new FakeClock());

        var pharmacist = await users.CreateAsync(new UserEditDto("new.pharm",
            "New Pharmacist", new[] { Role.Standard.Pharmacist }, 0m,
            "P12345"), "Starting#1");

        pharmacist.IsFailure.Should().BeTrue();
        pharmacist.Error.Code.Should().Be("SEC-030");

        var admin = await users.CreateAsync(new UserEditDto("new.admin",
            "New Admin", new[] { Role.Standard.Administrator }, 0m, null),
            "Starting#1");

        admin.IsFailure.Should().BeTrue();

        var cashier = await users.CreateAsync(new UserEditDto("new.cashier",
            "New Cashier", new[] { Role.Standard.Cashier }, 5m, null),
            "Starting#1");

        cashier.IsSuccess.Should().BeTrue(cashier.Error.Message);
    }
}
