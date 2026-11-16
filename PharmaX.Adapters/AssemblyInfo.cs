using System.Runtime.CompilerServices;

// The receipt document exposes its page rendering to the test project so
// that a test can draw a slip onto a bitmap and check what landed on it.
// A receipt is the one artefact a customer keeps and an auditor reads.
[assembly: InternalsVisibleTo("PharmaX.Tests.Unit")]
[assembly: InternalsVisibleTo("PharmaX.Desktop")]
