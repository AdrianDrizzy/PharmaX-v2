using System.Runtime.CompilerServices;

// GridExport projects a grid onto the report result the workbook writer
// takes. That projection is the whole of the accounts and ledger export,
// and it is testable without a desktop - so the test project sees it
// rather than the helper being made public for no other reason.
[assembly: InternalsVisibleTo("PharmaX.Tests.Unit")]
