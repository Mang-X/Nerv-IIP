// This assembly runs its gateway HTTP tests in parallel; it deliberately declares no
// CollectionBehavior, so xUnit's default per-class collections and default MaxParallelThreads apply
// and CI/local runs stay free to dial concurrency through runsettings.
//
// Host construction is the only process-wide hazard: Program.cs configures FastEndpoints through
// static state (`app.UseFastEndpoints(c => c.Serializer.Options.Converters.Add(...))`). That is
// handled precisely by BusinessGatewayTestHostGate, which excludes host construction from in-flight
// gateway requests, instead of by disabling parallelization for the whole assembly.
//
// Per-test downstream fakes are isolated by BusinessGatewayTestHost leases (a per-request scope
// header), not by serial execution. See BusinessGatewaySharedHostIsolationTests.
