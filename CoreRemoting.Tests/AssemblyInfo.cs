using Xunit;

// Disable parallel test execution to avoid interference between tests that use
// shared resources (global default client/server instances, fixed ports, etc.).
// This ensures the whole test suite can run reliably end-to-end.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
