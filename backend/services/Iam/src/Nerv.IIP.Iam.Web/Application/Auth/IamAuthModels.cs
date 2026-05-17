namespace Nerv.IIP.Iam.Web.Application.Auth;

public sealed record LoginRequest(string LoginName, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string? SessionId);
public sealed record ValidateConnectorCredentialRequest(string ConnectorHostId, string Secret);
public sealed record AuthResponse(string AccessToken, string RefreshToken, string SessionId);
public sealed record CurrentPrincipalResponse(string UserId, string LoginName, string Email, string PrincipalType);
public sealed record ConnectorPrincipalResponse(string PrincipalType, string OrganizationId, string EnvironmentId, string ConnectorHostId);
