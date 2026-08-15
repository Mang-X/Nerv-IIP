using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// Hands the gateway a fixed service-to-service bearer token, so a test can assert on exactly what
/// the gateway forwards downstream without standing up the real token acquisition.
/// </summary>
/// <remarks>
/// Every gateway test class used to declare its own private copy of this one-line record. The single
/// shared declaration keeps them from drifting apart; the token value stays per test, as the
/// constructor argument.
/// </remarks>
internal sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;
