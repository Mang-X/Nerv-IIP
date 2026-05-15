using Nerv.IIP.Caching;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNervIipCaching(builder.Configuration, "iam");
builder.Services.AddNervIipObservability(builder.Configuration, "iam");
builder.Services.AddSingleton<InMemoryIamStore>();

var app = builder.Build();
app.UseNervIipCorrelation();

app.MapGet("/health", () => "Healthy");
app.MapGet("/internal/iam/v1/build-info", () => new { service = "Iam", seeded = true });

app.MapPost("/api/iam/v1/auth/login", (LoginRequest request, InMemoryIamStore store) => TryAuth(() => store.Login(request.LoginName, request.Password)));
app.MapPost("/api/iam/v1/auth/refresh", (RefreshRequest request, InMemoryIamStore store) => TryAuth(() => store.Refresh(request.RefreshToken)));
app.MapPost("/api/iam/v1/auth/logout", (LogoutRequest request, InMemoryIamStore store) =>
{
    store.Logout(request.SessionId);
    return Results.NoContent();
});

app.MapPost("/api/iam/v1/connectors/credentials/validate", (ValidateConnectorCredentialRequest request, InMemoryIamStore store) => TryAuth(() => store.ValidateConnectorHost(request.ConnectorHostId, request.Secret)));

app.MapGet("/api/iam/v1/me", (HttpContext context, InMemoryIamStore store) =>
{
    var user = ValidateBearer(context, store);
    return user is null ? Results.Unauthorized() : Results.Ok(new { user.UserId, user.LoginName, user.Email, principalType = "user" });
});
app.MapGet("/api/iam/v1/users", (InMemoryIamStore store) => store.Users.Select(x => new { x.UserId, x.LoginName, x.Email, x.Enabled }));
app.MapPost("/api/iam/v1/users", () => Results.Created("/api/iam/v1/users/user-placeholder", new { userId = "user-placeholder" }));
app.MapPatch("/api/iam/v1/users/{userId}", (string userId) => Results.Ok(new { userId }));
app.MapPost("/api/iam/v1/users/{userId}/disable", (string userId) => Results.NoContent());
app.MapGet("/api/iam/v1/roles", (InMemoryIamStore store) => store.Roles);
app.MapPost("/api/iam/v1/roles", () => Results.Created("/api/iam/v1/roles/role-placeholder", new { roleId = "role-placeholder" }));
app.MapPatch("/api/iam/v1/roles/{roleId}/permissions", (string roleId) => Results.Ok(new { roleId }));
app.MapGet("/api/iam/v1/sessions", (InMemoryIamStore store) => store.Sessions);
app.MapPost("/api/iam/v1/sessions/{sessionId}/revoke", (string sessionId, InMemoryIamStore store) =>
{
    store.Logout(sessionId);
    return Results.NoContent();
});

app.Run();

static IResult TryAuth<T>(Func<T> action)
{
    try
    {
        return Results.Ok(action());
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status401Unauthorized);
    }
}

static Nerv.IIP.Iam.Domain.UserFact? ValidateBearer(HttpContext context, InMemoryIamStore store)
{
    var value = context.Request.Headers.Authorization.ToString();
    if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    try
    {
        return store.ValidateAccessToken(value["Bearer ".Length..]);
    }
    catch (UnauthorizedAccessException)
    {
        return null;
    }
}

public sealed record LoginRequest(string LoginName, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string SessionId);
public sealed record ValidateConnectorCredentialRequest(string ConnectorHostId, string Secret);

public partial class Program;
