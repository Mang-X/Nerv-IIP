using Xunit;

// These tests spin up multiple FastEndpoints WebApplicationFactory instances with
// per-test service overrides. Keep them serial inside the assembly to avoid shared
// endpoint/validator startup state from leaking between test hosts.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
