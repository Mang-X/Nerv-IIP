using System.Security.Claims;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Search;

[Tags("Business Console Search")]
[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
[HttpGet("/api/business-console/v1/search")]
[BusinessGatewayOperationId("searchBusinessConsoleObjects")]
public sealed class SearchBusinessConsoleObjectsEndpoint(BusinessConsoleSearchService search)
    : Endpoint<BusinessConsoleSearchRequest, ResponseData<BusinessConsoleSearchResponse>>
{
    public override async Task HandleAsync(BusinessConsoleSearchRequest req, CancellationToken ct)
    {
        var bearerToken = await HttpContext.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status401Unauthorized, "Unauthorized.", ct);
            return;
        }

        var organizationId = HttpContext.User.FindFirstValue("organizationId");
        var environmentId = HttpContext.User.FindFirstValue("environmentId");
        if (string.IsNullOrWhiteSpace(organizationId) || string.IsNullOrWhiteSpace(environmentId))
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status403Forbidden, "Forbidden.", ct);
            return;
        }

        var response = await search.SearchAsync(bearerToken, organizationId, environmentId, req, ct);
        await ResponseDataEndpointResults.WriteDataAsync(HttpContext, StatusCodes.Status200OK, response, ct);
    }
}

public sealed class BusinessConsoleSearchRequestValidator : Validator<BusinessConsoleSearchRequest>
{
    public BusinessConsoleSearchRequestValidator()
    {
        RuleFor(x => x.Q).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Types).MaximumLength(1000);
        RuleFor(x => x.Take).GreaterThanOrEqualTo(0).LessThanOrEqualTo(BusinessConsoleSearchService.MaxTake);
    }
}
