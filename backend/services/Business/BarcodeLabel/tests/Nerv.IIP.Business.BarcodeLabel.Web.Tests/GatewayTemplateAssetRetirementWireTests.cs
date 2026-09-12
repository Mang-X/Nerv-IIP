extern alias Gateway;

using System.Net;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TemplateAssetRetirementDecisionAggregate;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.TemplateAssetRetirements;
using Nerv.IIP.Contracts.BarcodeLabel;
using GatewayClient = Gateway::Nerv.IIP.BusinessGateway.Web.Application.BusinessServices.HttpBusinessBarcodeLabelClient;
using GatewaySigner = Gateway::Nerv.IIP.BusinessGateway.Web.Application.BusinessServices.TemplateAssetRetirementProofSigner;
using GatewayOptions = Gateway::Nerv.IIP.BusinessGateway.Web.Application.BusinessServices.BusinessGatewayTemplateAssetRetirementProofOptions;
using GatewayError = Gateway::Nerv.IIP.BusinessGateway.Web.Application.BusinessServices.BusinessServiceProxyException;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

// PublicContract #3043: fast lane, real Kestrel HTTP + production endpoint/verifier.
// The recording decision seam proves dispatch/no dispatch; PostgreSQL persistence remains #3042's separate evidence.
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class GatewayTemplateAssetRetirementWireTests
{
    [Fact]
    public async Task Gateway_signer_and_client_reach_production_endpoint_over_actual_http_and_reject_invalid_proofs()
    {
        var decisions = new RecordingSender();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("InternalService:BearerToken", "retirement-wire-token");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISender>();
                services.AddSingleton<ISender>(decisions);
                services.AddSingleton<TimeProvider>(new RetirementProofCases.Clock());
            });
        });
        factory.UseKestrel(0);
        using var factoryClient = factory.CreateClient();
        // A fresh SocketsHttpHandler cannot reach TestServer; this call must traverse the listening socket.
        using var http = new HttpClient(new SocketsHttpHandler()) { BaseAddress = factoryClient.BaseAddress };
        var client = new GatewayClient(http);
        var signer = new GatewaySigner(Options.Create(new GatewayOptions
        {
            Issuer = "business-gateway-test", Audience = "barcode-label-test",
            SecretBase64 = Convert.ToBase64String(RetirementProofCases.Secret),
        }), new RetirementProofCases.Clock());
        var signed = signer.Sign(RetirementProofCases.Request() with { Proof = "" }, "user-3043");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var response = await client.RetireTemplateAssetAsync("retirement-wire-token", signed, timeout.Token);
        Assert.Equal(decisions.DecisionId.Id, response.DecisionId);
        var command = Assert.Single(decisions.Commands);
        Assert.Equal("user-3043", command.RequesterSubject);
        Assert.Equal("business.barcodes.template-assets.retire", command.Permission);
        Assert.Equal(signed.Reason, command.Reason);
        Assert.Equal(signed.TemplateId, command.LabelTemplateId.Id);

        foreach (var (_, invalid) in RetirementProofCases.InvalidRequests(signed))
        {
            var error = await Assert.ThrowsAsync<GatewayError>(() =>
                client.RetireTemplateAssetAsync("retirement-wire-token", invalid, timeout.Token));
            Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
            Assert.Single(decisions.Commands);
        }
        var deniedBearer = await Assert.ThrowsAsync<GatewayError>(() =>
            client.RetireTemplateAssetAsync("wrong-internal-token", signed, timeout.Token));
        Assert.Equal(HttpStatusCode.Unauthorized, deniedBearer.StatusCode);
        Assert.Single(decisions.Commands);
    }

    [Theory]
    [InlineData("", "issuer", "audience")]
    [InlineData("invalid", "issuer", "audience")]
    [InlineData("YWJj", "issuer", "audience")]
    [InlineData("MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=", "", "audience")]
    [InlineData("MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=", "issuer", "")]
    public void Invalid_gateway_signer_configuration_cannot_produce_a_proof(string secret, string issuer, string audience)
    {
        var signer = new GatewaySigner(Options.Create(new GatewayOptions
        {
            SecretBase64 = secret, Issuer = issuer, Audience = audience,
        }), new RetirementProofCases.Clock());
        var error = Assert.Throws<InvalidOperationException>(() => signer.Sign(RetirementProofCases.Request(), "user"));
        Assert.Null(error.InnerException);
    }

    private sealed class RecordingSender : ISender
    {
        public TemplateAssetRetirementDecisionId DecisionId { get; } = new(Guid.NewGuid());
        public List<CreateTemplateAssetRetirementDecisionCommand> Commands { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Commands.Add(Assert.IsType<CreateTemplateAssetRetirementDecisionCommand>(request));
            return Task.FromResult((TResponse)(object)DecisionId);
        }
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
