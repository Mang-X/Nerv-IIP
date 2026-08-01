extern alias BusinessGateway;
extern alias MaintenanceWeb;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using BusinessGateway::Nerv.IIP.BusinessGateway.Web.Application.Auth;
using BusinessGateway::Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.DistributedLocking;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.DistributedLocks;
using StackExchange.Redis;
using GatewayProgram = BusinessGateway::Program;
using MaintenanceProgram = MaintenanceWeb::Program;

namespace Nerv.IIP.Business.FullChain.Tests;

public sealed class MaintenancePublicHttpLifecycleAcceptanceTests
{
    private const string OrganizationId = "org-man631-http";
    private const string EnvironmentId = "env-man631-http";
    private const string PrincipalId = "user-admin";
    private const string AlternateTechnicianId = "user-alternate";
    private const string TeamId = "team-man631-http";
    private const string InternalToken = "man631-public-chain-internal-token";

    [Fact]
    public async Task Alarm_report_walks_the_public_gateway_and_real_maintenance_http_chain_to_closed_readback()
    {
        await using var dependencies = await MaintenanceLifecycleDockerDependencies.StartAsync();
        var maintenanceLogs = new ErrorLogCapture();
        var integrationEvents = new RecordingIntegrationEventPublisher();
        await using var maintenanceFactory = CreateMaintenanceFactory(dependencies, maintenanceLogs, integrationEvents);
        await MigrateMaintenanceAsync(maintenanceFactory);
        using var maintenanceClient = maintenanceFactory.CreateClient();
        maintenanceClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", InternalToken);
        await AssertSuccessAsync(await maintenanceClient.PostAsJsonAsync(
            "/api/business/v1/maintenance/downtime-reasons",
            new
            {
                organizationId = OrganizationId,
                environmentId = EnvironmentId,
                reasonCode = "equipment-failure",
                description = "Equipment failure",
                reasonCategory = "breakdown",
                lossCategory = "equipment",
            }));

        var downstreamCapture = new DownstreamCapture();
        var gatewayLogs = new ErrorLogCapture();
        var authorization = new AllowedAuthorizationClient();
        var masterDataState = new MasterDataState();
        await using var gatewayFactory = CreateGatewayFactory(
            maintenanceFactory,
            downstreamCapture,
            gatewayLogs,
            authorization,
            masterDataState);
        using var browser = gatewayFactory.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PublicGatewayToken.ValidAccessToken(OrganizationId, EnvironmentId));

        var create = await browser.PostAsJsonAsync(
            "/api/business-console/v1/maintenance/work-orders",
            new
            {
                organizationId = OrganizationId,
                environmentId = EnvironmentId,
                deviceAssetId = "DEV-MAN631-HTTP",
                priority = "critical",
                sourceAlarmId = "ALARM-MAN631-HTTP",
                openedBy = "browser-alarm-report",
                idempotencyKey = "man631-http-create",
                assetUnavailableReason = "alarm-raised",
            });
        Assert.True(
            create.IsSuccessStatusCode,
            $"Gateway create failed: {await create.Content.ReadAsStringAsync()}; Maintenance transport: {downstreamCapture}; Maintenance logs: {maintenanceLogs}");
        var createData = await DataAsync(create);
        var workOrderId = createData.GetProperty("workOrderId").GetString();
        Assert.True(Guid.TryParse(workOrderId, out var strongWorkOrderId) && strongWorkOrderId != Guid.Empty);
        AssertConfirmedReceipt(createData, workOrderId!);

        var version = 0;
        var assignmentRequest = new
        {
            organizationId = OrganizationId,
            environmentId = EnvironmentId,
            technicianUserId = PrincipalId,
            teamId = TeamId,
            reason = "dispatch-to-on-duty-technician",
            idempotencyKey = "man631-http-assign",
            expectedVersion = version,
            scopeKind = "organization",
            scopeId = OrganizationId,
        };
        var assignData = await PostActionAsync(
            browser,
            workOrderId!,
            "/assignment",
            assignmentRequest,
            "Open",
            ++version,
            () => $"Maintenance transport: {downstreamCapture}; Gateway logs: {gatewayLogs}");
        AssertConfirmedReceipt(assignData, workOrderId!);

        var acceptData = await PostActionAsync(
            browser,
            workOrderId!,
            "/actions",
            new
            {
                organizationId = OrganizationId,
                environmentId = EnvironmentId,
                action = "accept",
                reason = "accept",
                idempotencyKey = "man631-http-accept",
                expectedVersion = version,
                scopeKind = "organization",
                scopeId = OrganizationId,
            },
            "Accepted",
            ++version,
            () => $"Maintenance transport: {downstreamCapture}; Gateway logs: {gatewayLogs}");
        AssertConfirmedReceipt(acceptData, workOrderId!);

        masterDataState.PrimaryWorkerActive = false;
        masterDataState.PrimaryMembershipCurrent = false;
        var authorizationChecksBeforeReplay = authorization.CheckCount;
        var delayedAssignmentReplay = await PostActionAsync(
            browser,
            workOrderId!,
            "/assignment",
            assignmentRequest,
            "Open",
            1,
            () => $"Maintenance transport: {downstreamCapture}; Gateway logs: {gatewayLogs}");
        Assert.Equal(assignData.GetProperty("changedAtUtc").GetDateTimeOffset(),
            delayedAssignmentReplay.GetProperty("changedAtUtc").GetDateTimeOffset());
        AssertConfirmedReceipt(delayedAssignmentReplay, workOrderId!);
        Assert.Equal(authorizationChecksBeforeReplay + 1, authorization.CheckCount);

        var createWithInvalidTarget = await DataAsync(await browser.PostAsJsonAsync(
            "/api/business-console/v1/maintenance/work-orders",
            new
            {
                organizationId = OrganizationId,
                environmentId = EnvironmentId,
                deviceAssetId = "DEV-MAN631-HTTP-INVALID-TARGET",
                priority = "normal",
                openedBy = "browser-invalid-target",
                idempotencyKey = "man631-http-create-invalid-target",
            }));
        var invalidTargetWorkOrderId = createWithInvalidTarget.GetProperty("workOrderId").GetString();
        var freshInvalidAssignment = await browser.PostAsJsonAsync(
            $"/api/business-console/v1/maintenance/work-orders/{invalidTargetWorkOrderId}/assignment",
            new
            {
                organizationId = OrganizationId,
                environmentId = EnvironmentId,
                technicianUserId = PrincipalId,
                teamId = TeamId,
                reason = "fresh-invalid-target",
                idempotencyKey = "man631-http-assign-invalid-target",
                expectedVersion = 0,
                scopeKind = "organization",
                scopeId = OrganizationId,
            });
        Assert.Equal(HttpStatusCode.Forbidden, freshInvalidAssignment.StatusCode);

        var changedPayloadReplay = await browser.PostAsJsonAsync(
            $"/api/business-console/v1/maintenance/work-orders/{workOrderId}/assignment",
            new
            {
                organizationId = OrganizationId,
                environmentId = EnvironmentId,
                technicianUserId = AlternateTechnicianId,
                teamId = (string?)null,
                reason = "dispatch-to-on-duty-technician",
                idempotencyKey = "man631-http-assign",
                expectedVersion = 0,
                scopeKind = "organization",
                scopeId = OrganizationId,
            });
        Assert.Equal(HttpStatusCode.Conflict, changedPayloadReplay.StatusCode);

        foreach (var step in new[]
                 {
                     new LifecycleStep("start", "InProgress", "man631-http-start"),
                     new LifecycleStep("complete", "Completed", "man631-http-complete"),
                     new LifecycleStep("verify", "Verified", "man631-http-verify"),
                     new LifecycleStep("close", "Closed", "man631-http-close"),
                 })
        {
            var actionData = await PostActionAsync(
                browser,
                workOrderId!,
                "/actions",
                new
                {
                    organizationId = OrganizationId,
                    environmentId = EnvironmentId,
                    action = step.Action,
                    reason = step.Action,
                    idempotencyKey = step.IdempotencyKey,
                    expectedVersion = version,
                    scopeKind = "organization",
                    scopeId = OrganizationId,
                    result = step.Action == "complete" ? "restored-by-public-http" : null,
                    downtimeReasonCode = step.Action == "complete" ? "equipment-failure" : null,
                    downtimeMinutes = step.Action == "complete" ? 15 : (int?)null,
                    actualLaborMinutes = step.Action == "complete" ? 20 : (int?)null,
                },
                step.ExpectedStatus,
                ++version,
                () => $"Maintenance transport: {downstreamCapture}; Gateway logs: {gatewayLogs}");
            AssertConfirmedReceipt(actionData, workOrderId!);
        }

        var readback = await browser.GetAsync(
            $"/api/business-console/v1/maintenance/work-orders/{workOrderId}" +
            $"?organizationId={OrganizationId}&environmentId={EnvironmentId}&scopeKind=organization&scopeId={OrganizationId}");
        var detail = await DataAsync(readback);

        Assert.Equal(workOrderId, detail.GetProperty("workOrderId").GetString());
        Assert.Equal("ALARM-MAN631-HTTP", detail.GetProperty("sourceAlarmId").GetString());
        Assert.Equal("Closed", detail.GetProperty("status").GetString());
        Assert.Equal(version, detail.GetProperty("version").GetInt32());
        Assert.Empty(detail.GetProperty("allowedActions").EnumerateArray());
        Assert.Equal(
            ["terminal-status"],
            detail.GetProperty("blockReasons").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(version, detail.GetProperty("lifecycle").GetArrayLength());
        Assert.Equal(
            ["Assign", "Accept", "Start", "Complete", "Verify", "Close"],
            detail.GetProperty("lifecycle").EnumerateArray().Select(item => item.GetProperty("action").GetString()));
        Assert.Contains(
            integrationEvents.Published,
            integrationEvent => integrationEvent.GetType().Name == "MaintenanceWorkOrderOpenedIntegrationEvent");
    }

    private static WebApplicationFactory<MaintenanceProgram> CreateMaintenanceFactory(
        MaintenanceLifecycleDockerDependencies dependencies,
        ErrorLogCapture logCapture,
        RecordingIntegrationEventPublisher integrationEvents) =>
        new WebApplicationFactory<MaintenanceProgram>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logCapture));
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("FastEndpoints:RestrictDiscoveryToEntryAssembly", "true");
            builder.UseSetting("ConnectionStrings:PostgreSQL", dependencies.PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", dependencies.RedisConnectionString);
            builder.UseSetting("IndustrialTelemetry:BaseUrl", "http://industrial-telemetry.test");
            builder.UseSetting("InternalService:BearerToken", InternalToken);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IConnectionMultiplexer>();
                services.RemoveAll<IRedisCommandLockStore>();
                services.RemoveAll<IDistributedLock>();
                services.RemoveAll<IIntegrationEventPublisher>();
                services.AddSingleton<IConnectionMultiplexer>(_ =>
                    ConnectionMultiplexer.Connect(dependencies.RedisConnectionString));
                services.AddSingleton<IRedisCommandLockStore>(provider =>
                    new StackExchangeRedisCommandLockStore(
                        provider.GetRequiredService<IConnectionMultiplexer>().GetDatabase(),
                        "business-maintenance"));
                services.AddSingleton<IDistributedLock>(provider =>
                    new RedisCommandDistributedLock(
                        provider.GetRequiredService<IRedisCommandLockStore>(),
                        TimeProvider.System));
                services.AddSingleton<IIntegrationEventPublisher>(integrationEvents);
            });
        });

    private static async Task MigrateMaintenanceAsync(WebApplicationFactory<MaintenanceProgram> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }

    private static WebApplicationFactory<GatewayProgram> CreateGatewayFactory(
        WebApplicationFactory<MaintenanceProgram> maintenanceFactory,
        DownstreamCapture capture,
        ErrorLogCapture logCapture,
        AllowedAuthorizationClient authorization,
        MasterDataState masterDataState) =>
        new WebApplicationFactory<GatewayProgram>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logCapture));
            builder.UseSetting("FastEndpoints:RestrictDiscoveryToEntryAssembly", "true");
            builder.UseSetting("Iam:Jwt:JwksJson", PublicGatewayToken.PublicJwksJson());
            builder.UseSetting("Iam:Jwt:Issuer", PublicGatewayToken.Issuer);
            builder.UseSetting("Iam:Jwt:Audience", PublicGatewayToken.Audience);
            builder.UseSetting("Maintenance:BaseUrl", "http://maintenance.test");
            builder.UseSetting("InternalService:BearerToken", InternalToken);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBusinessGatewayAuthorizationClient>();
                services.AddSingleton<IBusinessGatewayAuthorizationClient>(authorization);
                services.RemoveAll<IBusinessMasterDataClient>();
                services.AddSingleton(MasterDataProxy.Create(masterDataState));
                services.RemoveAll<IInternalServiceTokenProvider>();
                services.AddSingleton<IInternalServiceTokenProvider>(new StaticInternalServiceTokenProvider(InternalToken));
                services.RemoveAll<IBusinessMaintenanceClient>();
                services.AddHttpClient<IBusinessMaintenanceClient, HttpBusinessMaintenanceClient>(client =>
                    client.BaseAddress = new Uri("http://maintenance.test"))
                    .ConfigurePrimaryHttpMessageHandler(() =>
                        capture.Wrap(maintenanceFactory.Server.CreateHandler()));
            });
        });

    private static async Task<JsonElement> PostActionAsync(
        HttpClient browser,
        string workOrderId,
        string suffix,
        object body,
        string expectedStatus,
        int expectedVersion,
        Func<string> diagnostic)
    {
        var data = await DataAsync(await browser.PostAsJsonAsync(
            $"/api/business-console/v1/maintenance/work-orders/{workOrderId}{suffix}",
            body), diagnostic);
        Assert.Equal(workOrderId, data.GetProperty("workOrderId").GetString());
        Assert.Equal(expectedStatus, data.GetProperty("status").GetString());
        Assert.Equal(expectedVersion, data.GetProperty("version").GetInt32());
        Assert.NotEqual(default, data.GetProperty("changedAtUtc").GetDateTimeOffset());
        return data;
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response, Func<string>? diagnostic = null)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.IsSuccessStatusCode,
            $"Expected public HTTP success, got {(int)response.StatusCode}: {body}; {diagnostic?.Invoke()}");
        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        return document.RootElement.GetProperty("data").Clone();
    }

    private static async Task AssertSuccessAsync(HttpResponseMessage response)
    {
        _ = await DataAsync(response);
    }

    private static void AssertConfirmedReceipt(JsonElement data, string workOrderId)
    {
        var receipt = data.GetProperty("operationReceipt");
        Assert.Equal("confirmed", receipt.GetProperty("outcome").GetString());
        Assert.True(receipt.GetProperty("stateConfirmed").GetBoolean());
        Assert.False(receipt.GetProperty("readbackRequired").GetBoolean());
        Assert.Equal("maintenance-work-order", receipt.GetProperty("resourceType").GetString());
        Assert.Equal(workOrderId, receipt.GetProperty("resourceId").GetString());
    }

    private sealed record LifecycleStep(string Action, string ExpectedStatus, string IdempotencyKey);

    private sealed class DownstreamCapture
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public HttpStatusCode? StatusCode { get; private set; }
        public string? Body { get; private set; }

        public HttpMessageHandler Wrap(HttpMessageHandler inner) => new CaptureHandler(this, inner);

        public override string ToString() =>
            $"{Method} {RequestUri} => {(int?)StatusCode} {StatusCode}: {Body}";

        private sealed class CaptureHandler(DownstreamCapture owner, HttpMessageHandler inner) : HttpMessageHandler
        {
            private readonly HttpMessageInvoker invoker = new(inner, disposeHandler: true);

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                owner.Method = request.Method;
                owner.RequestUri = request.RequestUri;
                var response = await invoker.SendAsync(request, cancellationToken);
                owner.StatusCode = response.StatusCode;
                owner.Body = response.Content is null
                    ? null
                    : await response.Content.ReadAsStringAsync(cancellationToken);
                return response;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    invoker.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }

    private sealed class ErrorLogCapture : ILoggerProvider
    {
        private readonly List<string> entries = [];

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(entries, categoryName);

        public override string ToString() => string.Join(" | ", entries.TakeLast(10));

        public void Dispose()
        {
        }

        private sealed class CaptureLogger(List<string> entries, string categoryName) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    entries.Add($"{categoryName}: {formatter(state, exception)} {exception}");
                }
            }
        }
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = [];

        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Published.Add(integrationEvent!);
            return Task.CompletedTask;
        }
    }

    private sealed class AllowedAuthorizationClient : IBusinessGatewayAuthorizationClient
    {
        private int checkCount;

        public int CheckCount => Volatile.Read(ref checkCount);

        public Task<BusinessGatewayAuthorizationResult> CheckAsync(
            string bearerToken,
            BusinessGatewayPermissionRequirement requirement,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref checkCount);
            Assert.False(string.IsNullOrWhiteSpace(bearerToken));
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(BusinessGatewayAuthorizationResult.Allowed(
                PrincipalId,
                "user",
                "admin",
                scopeGrants:
                [
                    new AuthorizationScopeGrant(
                        "role", "maintenance-admin", "organization", OrganizationId,
                        [requirement.PermissionCode], OrganizationWide: true),
                ]));
        }
    }

    private sealed class StaticInternalServiceTokenProvider(string token) : IInternalServiceTokenProvider
    {
        public string BearerToken { get; } = token;
    }

    private class MasterDataProxy : DispatchProxy
    {
        private MasterDataState state = null!;

        public static IBusinessMasterDataClient Create(MasterDataState state)
        {
            var proxy = DispatchProxy.Create<IBusinessMasterDataClient, MasterDataProxy>();
            ((MasterDataProxy)(object)proxy).state = state;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            object result = targetMethod.Name switch
            {
                nameof(IBusinessMasterDataClient.GetPrincipalWorkContextAsync) =>
                    new BusinessMasterDataPrincipalWorkContextResponse(
                        "resolved", null, [], [], [], [], [],
                        [new BusinessMasterDataWorkContextCandidateScope(
                            "organization", OrganizationId, "MAN-631 organization", "organization", [])],
                        ["organization"], []),
                nameof(IBusinessMasterDataClient.GetResourceDetailAsync) => ResourceDetail(args),
                nameof(IBusinessMasterDataClient.ListTeamMembersAsync) =>
                    TeamMembers(args),
                nameof(IBusinessMasterDataClient.ListWorkersAsync) => WorkerDirectory(args),
                _ => throw new NotSupportedException(
                    $"MAN-631 public chain did not expect MasterData call '{targetMethod.Name}'."),
            };
            var resultType = targetMethod.ReturnType.GetGenericArguments().Single();
            return typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [result]);
        }

        private BusinessConsoleMasterDataResourceDetail ResourceDetail(object?[]? args)
        {
            var request = Assert.IsType<BusinessConsoleMasterDataResourceRequest>(args![1]);
            return new BusinessConsoleMasterDataResourceDetail(
                request.ResourceType,
                request.Code,
                request.Code,
                request.ResourceType != "team" || state.TeamActive,
                "man631-http-v1",
                request.OrganizationId,
                request.EnvironmentId);
        }

        private BusinessConsoleTeamMemberListResponse TeamMembers(object?[]? args)
        {
            var request = Assert.IsType<BusinessConsoleListTeamMembersRequest>(args![1]);
            Assert.Equal(OrganizationId, request.OrganizationId);
            Assert.Equal(EnvironmentId, request.EnvironmentId);
            Assert.Equal(TeamId, request.TeamCode);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return new BusinessConsoleTeamMemberListResponse(
                [new BusinessConsoleTeamMemberItem(
                    TeamId,
                    PrincipalId,
                    false,
                    today.AddDays(-30),
                    state.PrimaryMembershipCurrent ? null : today.AddDays(-1),
                    state.PrimaryMembershipCurrent,
                    "man631-membership-v1")],
                1);
        }

        private BusinessConsoleWorkerDirectoryResponse WorkerDirectory(object?[]? args)
        {
            var request = Assert.IsType<BusinessConsoleWorkerDirectoryRequest>(args![1]);
            Assert.Equal(OrganizationId, request.OrganizationId);
            Assert.Equal(EnvironmentId, request.EnvironmentId);
            Assert.Equal("active", request.EmploymentStatus);
            var active = request.UserId == AlternateTechnicianId
                || (request.UserId == PrincipalId && state.PrimaryWorkerActive);
            return new BusinessConsoleWorkerDirectoryResponse(
                active ? 1 : 0,
                2,
                1,
                active
                    ? [new BusinessConsoleWorkerDirectoryItem(
                    request.UserId!,
                    "EMP-MAN631",
                    "MAN-631 technician",
                    null,
                    null,
                    "Maintenance technician",
                    "active",
                    null,
                    true,
                    [],
                    [],
                    "man631-worker-v1")]
                    : []);
        }
    }

    private sealed class MasterDataState
    {
        public bool PrimaryWorkerActive { get; set; } = true;

        public bool PrimaryMembershipCurrent { get; set; } = true;

        public bool TeamActive { get; set; } = true;
    }

    private static class PublicGatewayToken
    {
        private const string Kid = "man631-public-chain-key";
        private static readonly RSA Rsa = RSA.Create(2048);

        public const string Issuer = "nerv-iip-man631-public-chain";
        public const string Audience = "nerv-iip-man631-business-gateway";

        public static string ValidAccessToken(string organizationId, string environmentId)
        {
            var now = DateTimeOffset.UtcNow;
            var token = new JwtSecurityToken(
                issuer: Issuer,
                audience: Audience,
                claims:
                [
                    new Claim(JwtRegisteredClaimNames.Sub, PrincipalId),
                    new Claim("sessionId", "man631-public-chain-session"),
                    new Claim("principalType", "user"),
                    new Claim("loginName", "admin"),
                    new Claim("securityStamp", "man631-public-chain-stamp"),
                    new Claim("permissionVersion", "1"),
                    new Claim("organizationId", organizationId),
                    new Claim("environmentId", environmentId),
                ],
                notBefore: now.AddMinutes(-1).UtcDateTime,
                expires: now.AddMinutes(15).UtcDateTime,
                signingCredentials: new SigningCredentials(
                    new RsaSecurityKey(Rsa) { KeyId = Kid },
                    SecurityAlgorithms.RsaSha256));
            token.Header["kid"] = Kid;
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public static string PublicJwksJson()
        {
            var parameters = Rsa.ExportParameters(false);
            return $$"""
                {"keys":[{"kty":"RSA","use":"sig","kid":"{{Kid}}","alg":"RS256","n":"{{Base64UrlEncoder.Encode(parameters.Modulus)}}","e":"{{Base64UrlEncoder.Encode(parameters.Exponent)}}"}]}
                """;
        }
    }
}
