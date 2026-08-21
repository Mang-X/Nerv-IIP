using System.Net;
using System.Text;
using MediatR;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.StandardOperationAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands.StandardOperations;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class ProductEngineeringDynamicProviderContractTests
{
    private const string ReleaseCommandsPath =
        "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductEngineeringReleaseCommands.cs";
    private const string StandardOperationCommandsPath =
        "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/StandardOperations/StandardOperationCommands.cs";

    private const string EbomReleaseMessage = "EBOM 发布失败，请检查物料行和生效日期。";
    private const string MbomReleaseMessage = "MBOM 发布失败，请检查物料行、配方和来源 EBOM。";
    private const string RoutingReleaseMessage = "工艺路线发布失败，请检查工序和生效日期。";
    private const string StandardOperationCreateMessage = "标准工序创建失败，请检查工序参数。";
    private const string StandardOperationUpdateMessage = "标准工序更新失败，请检查工序参数。";
    private const string StandardOperationArchiveMessage = "标准工序归档失败，请检查状态和归档原因。";
    private const string ProviderUnavailableMessage = "主数据引用校验服务暂不可用，请稍后重试。";
    private const string ProviderEnvelopeMessage = "主数据引用校验返回无效结果，请稍后重试。";
    private const string ProviderReferenceMessage = "存在缺失或未启用的主数据引用，请检查后重试。";

    private static readonly IReadOnlyCollection<DynamicWrapperTarget> DynamicWrapperTargets =
    [
        new("ProductEngineeringReleaseCommands.cs", "ReleaseEngineeringBomCommandHandler", EbomReleaseMessage),
        new("ProductEngineeringReleaseCommands.cs", "ReleaseManufacturingBomCommandHandler", MbomReleaseMessage),
        new("ProductEngineeringReleaseCommands.cs", "ReleaseRoutingCommandHandler", RoutingReleaseMessage),
        new("StandardOperationCommands.cs", "CreateStandardOperationCommandHandler", StandardOperationCreateMessage),
        new("StandardOperationCommands.cs", "UpdateStandardOperationCommandHandler", StandardOperationUpdateMessage),
        new("StandardOperationCommands.cs", "ArchiveStandardOperationCommandHandler", StandardOperationArchiveMessage),
    ];

    [Fact]
    public void Dynamic_wrappers_have_a_closed_operation_message_ledger()
    {
        var repositoryRoot = FindRepositoryRoot();

        foreach (var target in DynamicWrapperTargets)
        {
            var path = Path.Combine(
                repositoryRoot,
                "backend",
                "services",
                "Business",
                "ProductEngineering",
                "src",
                "Nerv.IIP.Business.ProductEngineering.Web",
                "Application",
                "Commands",
                target.FileName == "StandardOperationCommands.cs" ? "StandardOperations" : string.Empty,
                target.FileName);
            var source = File.ReadAllText(path);
            var root = CSharpSyntaxTree.ParseText(source, path: target.FileName).GetRoot();
            var handler = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Single(type => type.Identifier.ValueText == target.HandlerTypeName);
            var handle = handler.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == "Handle");
            var wrapper = handle.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(invocation => invocation.Expression is MemberAccessExpressionSyntax memberAccess
                    && memberAccess.Name.Identifier.ValueText == "AsKnownException");

            Assert.Equal(2, wrapper.ArgumentList.Arguments.Count);
            var message = Assert.IsType<LiteralExpressionSyntax>(wrapper.ArgumentList.Arguments[1].Expression);
            Assert.Equal(target.ExpectedMessage, message.Token.ValueText);
            Assert.DoesNotContain("exception.Message", wrapper.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Dynamic_helper_uses_the_operation_message_for_all_four_catches()
    {
        var repositoryRoot = FindRepositoryRoot();
        var path = Path.Combine(repositoryRoot, ReleaseCommandsPath.Replace('/', Path.DirectorySeparatorChar));
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: ReleaseCommandsPath).GetRoot();
        var helper = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == "ProductEngineeringReleaseValidation");
        var constructions = helper.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText is "AsKnownException")
            .SelectMany(method => method.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            .Where(creation => creation.Type.ToString() == "KnownException")
            .ToArray();

        Assert.Equal(4, constructions.Length);
        Assert.All(constructions, construction =>
        {
            Assert.DoesNotContain("exception.Message", construction.ToString(), StringComparison.Ordinal);
            Assert.Equal("resolvedMessage", construction.ArgumentList!.Arguments[0].Expression.ToString());
        });
    }

    [Fact]
    public void Provider_boundary_has_three_fixed_user_messages()
    {
        var repositoryRoot = FindRepositoryRoot();
        var path = Path.Combine(repositoryRoot, ReleaseCommandsPath.Replace('/', Path.DirectorySeparatorChar));
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: ReleaseCommandsPath).GetRoot();
        var validator = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == "HttpProductEngineeringMasterDataReferenceValidator");
        var method = validator.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(candidate => candidate.Identifier.ValueText == "ValidateActiveReferencesAsync");
        var messages = method.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Where(creation => creation.Type.ToString() == "KnownException")
            .Select(creation => Assert.IsType<LiteralExpressionSyntax>(creation.ArgumentList!.Arguments[0].Expression).Token.ValueText)
            .ToArray();

        Assert.Equal(
            [ProviderUnavailableMessage, ProviderEnvelopeMessage, ProviderReferenceMessage],
            messages);
    }

    [Fact]
    public async Task Create_standard_operation_hides_dynamic_domain_message()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateStandardOperationCommandHandler(new StandardOperationRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            NewCreateCommand("OP-DYNAMIC") with { StandardRunMinutes = 0 },
            CancellationToken.None));

        Assert.Equal(StandardOperationCreateMessage, exception.Message);
        Assert.IsAssignableFrom<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public async Task Update_standard_operation_hides_dynamic_domain_message()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.StandardOperations.Add(StandardOperation.Create(
            "org-001", "env-dev", "OP-DYNAMIC", "混合", "WC-MIX-01", 5, 30, "INHOUSE", true, false, false, null));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new UpdateStandardOperationCommandHandler(new StandardOperationRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new UpdateStandardOperationCommand(
                "org-001", "env-dev", "OP-DYNAMIC", "精混", "WC-MIX-02", 8, 0, "INHOUSE-QC", true, true, false, null),
            CancellationToken.None));

        Assert.Equal(StandardOperationUpdateMessage, exception.Message);
        Assert.IsAssignableFrom<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public async Task Http_provider_hides_non_success_status_and_reason()
    {
        using var client = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            ReasonPhrase = "provider-secret",
        });
        var validator = new HttpProductEngineeringMasterDataReferenceValidator(client, new TestTokenProvider());

        var exception = await Assert.ThrowsAsync<KnownException>(() => validator.ValidateActiveReferencesAsync(
            "org-001", "env-dev", [new ProductEngineeringMasterDataReference("sku", "SKU-001")], CancellationToken.None));

        Assert.Equal(ProviderUnavailableMessage, exception.Message);
        Assert.DoesNotContain("provider-secret", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("503", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_provider_hides_empty_envelope_and_provider_message()
    {
        using var client = CreateHttpClient(_ => JsonResponse(
            """{"success":false,"message":"provider-secret","code":500,"data":null}"""));
        var validator = new HttpProductEngineeringMasterDataReferenceValidator(client, new TestTokenProvider());

        var exception = await Assert.ThrowsAsync<KnownException>(() => validator.ValidateActiveReferencesAsync(
            "org-001", "env-dev", [new ProductEngineeringMasterDataReference("sku", "SKU-001")], CancellationToken.None));

        Assert.Equal(ProviderEnvelopeMessage, exception.Message);
        Assert.DoesNotContain("provider-secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_provider_hides_invalid_reference_details()
    {
        using var client = CreateHttpClient(_ => JsonResponse(
            """{"success":true,"message":"provider-secret","code":0,"data":{"valid":false,"references":[{"resourceType":"sku","code":"SKU-001","exists":false,"active":true,"displayName":"display-secret","snapshotVersion":"snapshot-secret","disabledReason":"reason-secret"}]}}"""));
        var validator = new HttpProductEngineeringMasterDataReferenceValidator(client, new TestTokenProvider());

        var exception = await Assert.ThrowsAsync<KnownException>(() => validator.ValidateActiveReferencesAsync(
            "org-001", "env-dev", [new ProductEngineeringMasterDataReference("sku", "SKU-001")], CancellationToken.None));

        Assert.Equal(ProviderReferenceMessage, exception.Message);
        Assert.DoesNotContain("provider-secret", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("display-secret", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("snapshot-secret", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("reason-secret", exception.Message, StringComparison.Ordinal);
    }

    private static CreateStandardOperationCommand NewCreateCommand(string operationCode) =>
        new(
            "org-001", "env-dev", operationCode, "混合", "WC-MIX-01", 5, 30, "INHOUSE", true, false, false, null);

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) =>
        new(new StubHttpMessageHandler(responseFactory))
        {
            BaseAddress = new Uri("https://master-data.test"),
        };

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase($"product-engineering-dynamic-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed record DynamicWrapperTarget(string FileName, string HandlerTypeName, string ExpectedMessage);

    private sealed class TestTokenProvider : IInternalServiceTokenProvider
    {
        public string BearerToken => "test-internal-token";
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
