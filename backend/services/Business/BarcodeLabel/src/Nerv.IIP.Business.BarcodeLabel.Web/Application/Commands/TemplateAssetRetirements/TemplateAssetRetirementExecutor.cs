using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Net.Http.Json;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TemplateAssetRetirementDecisionAggregate;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure.Retirement;
using Nerv.IIP.Contracts.FileStorage;
using BarcodeLabelIntegrationEventSources = Nerv.IIP.Contracts.BarcodeLabel.BarcodeLabelIntegrationEventSources;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.TemplateAssetRetirements;

public sealed record TemplateAssetRetirementExecutorOptions(
    byte[] Key, string Issuer, string Audience, long ClientWindowSeconds, long LeaseSeconds, long MaxBackoffSeconds)
{
    public const string Section = "FileStorage:TemplateAssetRetirement";

    public static TemplateAssetRetirementExecutorOptions Load(IConfiguration config)
    {
        try
        {
            var key = Convert.FromBase64String(config[$"{Section}:Secret"] ?? "");
            var issuer = config[$"{Section}:Issuer"];
            var audience = config[$"{Section}:Audience"];
            var window = config.GetValue<long?>($"{Section}:ClientWindowSeconds") ?? 2592000;
            var lease = config.GetValue<long?>($"{Section}:LeaseSeconds") ?? 300;
            var backoff = config.GetValue<long?>($"{Section}:MaxBackoffSeconds") ?? 300;
            if (key.Length < 32 || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience)
                || window <= 0 || lease <= 0 || backoff <= 0 || (decimal)lease + 2m * backoff > 7776000)
                throw new ArgumentException();
            return new(key, issuer, audience, window, lease, backoff);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            throw new InvalidOperationException($"{Section} requires a Base64 secret of at least 32 bytes, issuer/audience, and valid retirement replay inputs.");
        }
    }
}

public interface ITemplateAssetRetirementSigner
{
    RetireTemplateAssetRequest Sign(TemplateAssetRetirementDecision decision);
}

public sealed class TemplateAssetRetirementSigner(TemplateAssetRetirementExecutorOptions options, TimeProvider clock)
    : ITemplateAssetRetirementSigner
{
    public RetireTemplateAssetRequest Sign(TemplateAssetRetirementDecision decision)
    {
        var issued = clock.GetUtcNow().ToUnixTimeSeconds();
        string[] fields = ["1", "HMAC-SHA-256", options.Issuer, options.Audience,
            Number(issued), Number(issued + 300), decision.Id.Id.ToString("D"),
            Number(decision.ReplayPolicyVersion!.Value), Number(decision.ClientWindowSeconds!.Value),
            Number(decision.ExecutorLeaseSeconds!.Value), Number(decision.ExecutorMaxBackoffSeconds!.Value),
            decision.OrganizationId, decision.EnvironmentId, decision.TemplateFileId, decision.TemplateAssetSha256,
            BarcodeLabelIntegrationEventSources.BusinessBarcodeLabel, "label-template", decision.TemplateCode, "barcode-label-template"];
        var payload = Encoding.UTF8.GetBytes(string.Join('\n', fields.Select(value =>
            $"{Number(Encoding.UTF8.GetByteCount(value))}:{value}")));
        return new(Encode(payload), Encode(HMACSHA256.HashData(options.Key, payload)));
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class TemplateAssetRetirementClient(HttpClient http)
{
    public async Task<RetireTemplateAssetResponse> SendAsync(RetireTemplateAssetRequest proof, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("/internal/file-storage/v1/template-asset-retirements", proof, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RetireTemplateAssetResponse>(ct)
            ?? throw new HttpRequestException("FileStorage returned an empty retirement receipt.");
    }
}

public sealed class TemplateAssetRetirementExecutor(TemplateAssetRetirementExecutionStore store,
    ITemplateAssetRetirementSigner signer, TemplateAssetRetirementClient client,
    TemplateAssetRetirementExecutorOptions options, TimeProvider clock)
{
    public async Task<bool> ExecuteNextAsync(CancellationToken ct)
    {
        var decision = await store.ClaimAsync(options.ClientWindowSeconds, options.LeaseSeconds, options.MaxBackoffSeconds, ct);
        if (decision is null) return false;
        if (clock.GetUtcNow() >= decision.RecoveryUntilUtc)
        {
            await store.RetryAsync(decision, ct);
            return true;
        }

        try
        {
            var receipt = await client.SendAsync(signer.Sign(decision), ct);
            // The remote receipt is a trust boundary; never assign another decision's outcome.
            if (receipt.DecisionId != decision.Id.Id.ToString("D") || receipt.FileId != decision.TemplateFileId
                || receipt.Status != "physical-hold" || receipt.ReplayHorizonSeconds is < 691200 or > 7776000)
                throw new HttpRequestException("FileStorage returned an invalid retirement receipt.");
            await store.CompleteAsync(decision, receipt.QuotaReleasedAtUtc, receipt.ReplayHorizonSeconds, ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException
            || exception is OperationCanceledException && !ct.IsCancellationRequested)
        {
            await store.RetryAsync(decision, ct);
        }
        return true;
    }
}

public sealed class TemplateAssetRetirementWorker(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<TemplateAssetRetirementWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var executor = scope.ServiceProvider.GetRequiredService<TemplateAssetRetirementExecutor>();
                if (await executor.ExecuteNextAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                // Persisted leases recover interrupted attempts. Do not log remote payloads or credentials.
                logger.LogError("Template asset retirement scan failed ({ExceptionType}).", exception.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken);
        }
    }
}
