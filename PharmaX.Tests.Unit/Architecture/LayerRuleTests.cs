using System.Reflection;
using FluentAssertions;

namespace PharmaX.Tests.Unit.Architecture;

/// <summary>
/// Good intentions do not survive a deadline, so the dependency rule of
/// Section 4.1 is a test that fails the build when a layer reaches
/// somewhere it should not.
/// </summary>
public class LayerRuleTests
{
    private static Assembly Domain =>
        typeof(PharmaX.Domain.Sales.Sale).Assembly;

    private static Assembly App =>
        typeof(PharmaX.Application.Sales.SaleService).Assembly;

    private static Assembly Desktop =>
        typeof(PharmaX.Desktop.Features.Sales.PosPresenter).Assembly;

    [Fact]
    public void Domain_references_nothing_but_the_framework()
    {
        var offenders = Domain.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => !n.StartsWith("System", StringComparison.Ordinal)
                     && !n.StartsWith("Microsoft.CSharp", StringComparison.Ordinal)
                     && n != "netstandard")
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void Application_does_not_reference_EF_Core_or_WinForms()
    {
        var offenders = App.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.Contains("EntityFrameworkCore", StringComparison.Ordinal)
                     || n.Contains("Windows.Forms", StringComparison.Ordinal))
            .ToList();

        offenders.Should().BeEmpty();
    }

    /// <summary>
    /// The test that pays for itself. A presenter quietly taking a Form
    /// parameter is the most common way this architecture erodes.
    /// </summary>
    [Fact]
    public void Presenters_do_not_reference_WinForms_types()
    {
        // Presenters live in PharmaX.Desktop but must stay UI-toolkit free.
        var presenters = Desktop.GetTypes()
            .Where(t => t.Name.EndsWith("Presenter", StringComparison.Ordinal))
            .Where(t => !t.IsAbstract)
            .ToList();

        presenters.Should().NotBeEmpty(
            "the rule is worthless if it finds no presenters to check");

        foreach (var presenter in presenters)
        {
            var leaks = presenter
                .GetProperties(BindingFlags.Instance | BindingFlags.NonPublic
                               | BindingFlags.Public)
                .Where(x => IsWinForms(x.PropertyType))
                .ToList();

            leaks.Should().BeEmpty(
                $"{presenter.Name} must not hold a Windows Forms type");

            // Constructor parameters too: a presenter handed a Form is
            // untestable whether or not it keeps it in a property.
            var injected = presenter.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Where(p => IsWinForms(p.ParameterType))
                .ToList();

            injected.Should().BeEmpty(
                $"{presenter.Name} must not take a Windows Forms type");
        }
    }

    /// <summary>
    /// A view interface is the boundary, so it must not leak a control
    /// either: IPosView returning a DataGridViewRow would put the grid
    /// back inside the presenter by the side door.
    /// </summary>
    [Fact]
    public void View_interfaces_do_not_expose_WinForms_types()
    {
        var views = Desktop.GetTypes()
            .Where(t => t.IsInterface)
            .Where(t => t.Name.StartsWith('I')
                        && t.Name.EndsWith("View", StringComparison.Ordinal))
            .ToList();

        views.Should().NotBeEmpty();

        foreach (var view in views)
        {
            var leaks = view.GetMembers()
                .Select(m => m switch
                {
                    PropertyInfo p => p.PropertyType,
                    MethodInfo mi => mi.ReturnType,
                    _ => typeof(void)
                })
                .Concat(view.GetMethods()
                    .SelectMany(m => m.GetParameters())
                    .Select(p => p.ParameterType))
                .Where(IsWinForms)
                .ToList();

            leaks.Should().BeEmpty(
                $"{view.Name} must not expose a Windows Forms type");
        }
    }

    private static bool IsWinForms(Type type) =>
        type.Namespace?.StartsWith("System.Windows.Forms",
                                   StringComparison.Ordinal) == true;
}
