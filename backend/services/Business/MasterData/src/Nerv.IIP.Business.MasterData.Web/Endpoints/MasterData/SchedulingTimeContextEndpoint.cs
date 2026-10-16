using FastEndpoints;
using FluentValidation;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Business.MasterData.Web.Endpoints.MasterData;

public sealed class ResolveSchedulingTimeContextEndpoint(ApplicationDbContext db, IConfiguration configuration)
    : MasterDataEndpoint<SchedulingTimeContextRequest, ResponseData<IReadOnlyCollection<SchedulingTimeContextItem>>>
{
    public override void Configure()
    {
        ConfigureMasterDataContract(MasterDataEndpointContracts.Get<ResolveSchedulingTimeContextEndpoint>());
    }

    public override async Task HandleAsync(SchedulingTimeContextRequest req, CancellationToken ct)
    {
        if (!SchedulingTimeContextAuthorization.Verify(configuration[SchedulingTimeContextAuthorization.ConfigurationKey], req,
            HttpContext.Request.Headers[SchedulingTimeContextAuthorization.HeaderName].ToString(), DateTimeOffset.UtcNow))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }
        await Send.OkAsync((await SchedulingTimeContextQuery.ResolveAsync(db, req, ct)).AsResponseData(), ct);
    }
}

public sealed class SchedulingTimeContextRequestValidator : Validator<SchedulingTimeContextRequest>
{
    public SchedulingTimeContextRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.EnvironmentId).NotEmpty();
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.PrincipalId).NotEmpty();
        RuleFor(x => x.WorkCenterCodes).NotNull();
        RuleForEach(x => x.WorkCenterCodes).NotEmpty();
    }
}
