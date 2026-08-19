// PlatformGateway HTTP tests run with xUnit's default per-class parallelization. Each test still
// owns its WebApplicationFactory and downstream fakes; PlatformGatewayTestHostGate narrows the one
// process-wide hazard by excluding FastEndpoints host construction from in-flight requests.
// PlatformGatewayHostIsolationTests protects these observable boundaries.
