namespace Nerv.IIP.ConnectorHost.Connectors.Abstractions;

public sealed record RecordIndustrialTelemetrySampleRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string TagKey,
    DateTimeOffset BucketStartUtc,
    DateTimeOffset BucketEndUtc,
    int SampleCount,
    decimal MinValue,
    decimal MaxValue,
    decimal AverageValue,
    string SourceSequence,
    string? SourceSystem,
    string? SourceConnector,
    string? DeviceState = null,
    DateTimeOffset? StateOccurredAtUtc = null,
    decimal? FirstValue = null,
    decimal? LastValue = null,
    string? CollectionConnectorId = null);

public interface IIndustrialTelemetrySamplesClient
{
    Task RecordSampleAsync(
        RecordIndustrialTelemetrySampleRequest request,
        CancellationToken cancellationToken);
}
