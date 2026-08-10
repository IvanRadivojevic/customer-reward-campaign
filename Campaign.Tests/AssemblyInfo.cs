using Xunit;

// All tests share one database, and the grant rules run at SERIALIZABLE. On a table this small the
// range locks reach beyond the rows a single test owns, so two collections running at once make each
// other fail for reasons that have nothing to do with the rule under test. Collections therefore run
// one at a time. The parallel tests still fire their twelve simultaneous requests inside a single
// test - that is the concurrency the rules are about, and it is untouched by this.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
