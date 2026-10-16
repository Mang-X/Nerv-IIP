using Nerv.IIP.Contracts.MasterData;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed class SchedulingTimeContextClient(HttpClient http, IConfiguration configuration) : BusinessServiceHttpClient(http)
{
    public Task<IReadOnlyCollection<SchedulingTimeContextItem>> ResolveAsync(string token, SchedulingTimeContextRequest request, CancellationToken ct)
    {
        var proof = SchedulingTimeContextAuthorization.Sign(
            configuration[SchedulingTimeContextAuthorization.ConfigurationKey]
                ?? throw new InvalidOperationException("Scheduling time context signing key is not configured."),
            request, DateTimeOffset.UtcNow);
        return SendAsync<IReadOnlyCollection<SchedulingTimeContextItem>>(token, HttpMethod.Post,
            "/api/business/v1/master-data/scheduling-time-context/resolve", request, ct,
            configureRequest: message => message.Headers.Add(SchedulingTimeContextAuthorization.HeaderName, proof));
    }
}
