using MediatR;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Auth;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands;
using Nerv.IIP.Business.ProductEngineering.Web.Endpoints.ProductEngineering;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class ProductEngineeringReleaseApiContractTests
{
    [Fact]
    public void Product_engineering_release_endpoint_contracts_cover_issue_127_surface()
    {
        var contracts = ProductEngineeringEndpointContracts.All;

        Assert.Equal(7, contracts.Count);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/documents" && x.PermissionCode == EngineeringPermissionCodes.DocumentsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/engineering-boms/release" && x.PermissionCode == EngineeringPermissionCodes.BomsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/manufacturing-boms/release" && x.PermissionCode == EngineeringPermissionCodes.BomsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/routings/release" && x.PermissionCode == EngineeringPermissionCodes.BomsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/engineering-changes/release" && x.PermissionCode == EngineeringPermissionCodes.ChangesManage);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/engineering-boms" && x.PermissionCode == EngineeringPermissionCodes.BomsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/routings" && x.PermissionCode == EngineeringPermissionCodes.BomsRead);
        Assert.All(contracts, contract =>
        {
            Assert.StartsWith("/api/business/v1/engineering", contract.Route, StringComparison.Ordinal);
            Assert.Contains(contract.PermissionCode, EngineeringPermissionCodes.All);
            Assert.Matches("^[a-z][A-Za-z0-9]*$", contract.OperationId);
            Assert.Contains("Business", contract.OperationId, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(typeof(RegisterEngineeringDocumentEndpoint))]
    [InlineData(typeof(ReleaseEngineeringBomEndpoint))]
    [InlineData(typeof(ReleaseManufacturingBomEndpoint))]
    [InlineData(typeof(ReleaseRoutingEndpoint))]
    [InlineData(typeof(ReleaseEngineeringChangeEndpoint))]
    [InlineData(typeof(ListEngineeringBomsEndpoint))]
    [InlineData(typeof(ListRoutingsEndpoint))]
    public void Product_engineering_release_endpoints_route_through_mediator(Type endpointType)
    {
        var parameterTypes = endpointType
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
        Assert.DoesNotContain(typeof(ApplicationDbContext), parameterTypes);
    }

    [Fact]
    public void Release_command_validators_reject_blank_scope_and_file_references()
    {
        var documentValidator = new RegisterEngineeringDocumentCommandValidator();
        var documentResult = documentValidator.Validate(new RegisterEngineeringDocumentCommand(
            "",
            "env-dev",
            "DOC-1000",
            "A",
            "",
            "pump.dwg",
            "application/dwg",
            "cad-drawing"));

        Assert.False(documentResult.IsValid);
        Assert.Contains(documentResult.Errors, x => x.PropertyName == nameof(RegisterEngineeringDocumentCommand.OrganizationId));
        Assert.Contains(documentResult.Errors, x => x.PropertyName == nameof(RegisterEngineeringDocumentCommand.FileId));

        var bomValidator = new ReleaseEngineeringBomCommandValidator();
        var bomResult = bomValidator.Validate(new ReleaseEngineeringBomCommand(
            "org-001",
            "",
            "EBOM-1000",
            "A",
            "ENG-1000",
            new DateOnly(2026, 6, 1),
            []));

        Assert.False(bomResult.IsValid);
        Assert.Contains(bomResult.Errors, x => x.PropertyName == nameof(ReleaseEngineeringBomCommand.EnvironmentId));
        Assert.Contains(bomResult.Errors, x => x.PropertyName == nameof(ReleaseEngineeringBomCommand.Lines));
    }
}
