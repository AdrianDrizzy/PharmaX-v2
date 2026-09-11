using Xunit;

// Integration tests run one at a time.
//
// Two reasons, both real. The currency a site trades in is a static on
// Money, set once at start-up in the real application; a test that
// changes it to prove the switch works cannot run alongside tests that
// are asserting amounts in the old one. And these tests each stand up
// their own SQLite file and container, so the wall-clock cost of
// serialising them is a second or two against a suite that takes three.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
