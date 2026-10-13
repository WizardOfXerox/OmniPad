using Xunit;

// Disable parallel test execution across test classes in OmniPadServer.Tests.
// Backend simulation tests touch process/static simulator states (MacInputSimulator,
// VirtualMouseKeyboardBackend, Linux uinput structures, and SessionManager timeouts).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
