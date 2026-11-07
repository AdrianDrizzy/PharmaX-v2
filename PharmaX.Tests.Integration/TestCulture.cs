using System.Globalization;
using System.Runtime.CompilerServices;

namespace PharmaX.Tests.Integration;

internal static class TestCulture
{
    // The app sets its own culture at start-up (Program.ApplyCurrency, US
    // dollars by default); the tests read amounts as it prints them, whatever
    // region the PC running them is set to.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Pin() =>
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture =
            CultureInfo.GetCultureInfo("en-US");
}
