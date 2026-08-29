using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Nerv.IIP.Business.MasterData.Web.Endpoints.MasterData;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MasterDataOpenApiTests
{
    [Fact]
    public async Task OpenApi_document_exposes_contract_operation_ids()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var document = await GetOpenApiDocumentAsync(client);
        AssertOperationIdsAreUnique(document);

        foreach (var contract in MasterDataEndpointContracts.All)
        {
            Assert.Equal(
                contract.OperationId,
                GetOperationId(document, contract.Route, contract.HttpMethod.ToLowerInvariant()));
        }
    }

    [Fact]
    public async Task OpenApi_document_exposes_tooling_directory_query_and_response_contract()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var document = await GetOpenApiDocumentAsync(client);
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/business/v1/master-data/tooling-assets")
            .GetProperty("get");
        var parameterNames = operation.GetProperty("parameters")
            .EnumerateArray()
            .Select(parameter => parameter.GetProperty("name").GetString())
            .ToArray();
        var operationJson = operation.GetRawText();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var toolingItemSchema = schemas.EnumerateObject()
            .Single(schema => schema.Name.EndsWith("ToolingAssetListItem", StringComparison.Ordinal))
            .Value;
        var responsePropertyNames = toolingItemSchema.GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var statusResponseSchema = ResolveSchema(
            toolingItemSchema.GetProperty("properties").GetProperty("status"),
            schemas);
        var statusRequestSchema = ResolveSchema(
            operation.GetProperty("parameters")
                .EnumerateArray()
                .Single(parameter => parameter.GetProperty("name").GetString() == "status")
                .GetProperty("schema"),
            schemas);

        Assert.Equal("listBusinessMasterDataToolingAssets", operation.GetProperty("operationId").GetString());
        Assert.Contains("organizationId", parameterNames);
        Assert.Contains("environmentId", parameterNames);
        Assert.Contains("keyword", parameterNames);
        Assert.Contains("status", parameterNames);
        Assert.Contains("skip", parameterNames);
        Assert.Contains("take", parameterNames);
        Assert.Contains("ToolingAssetListResponse", operationJson, StringComparison.Ordinal);
        Assert.Equal(
            [
                "code",
                "isSchedulable",
                "maintenanceLifeCount",
                "name",
                "skuCodes",
                "status",
                "toolingType",
                "usageCount",
                "workCenterCodes",
            ],
            responsePropertyNames);
        AssertStringToolingStatusSchema(statusResponseSchema);
        AssertStringToolingStatusSchema(statusRequestSchema);
    }

    [Fact]
    public async Task OpenApi_document_keeps_master_data_resource_list_query_flat_and_compatible()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var document = await GetOpenApiDocumentAsync(client);
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/business/v1/master-data/resources")
            .GetProperty("get");
        var parameters = operation.GetProperty("parameters").EnumerateArray().ToArray();
        var parameterNames = parameters
            .Select(parameter => parameter.GetProperty("name").GetString()!)
            .ToArray();

        Assert.Equal(
            [
                "organizationId",
                "environmentId",
                "resourceType",
                "includeDisabled",
                "skip",
                "take",
                "codeSet",
                "parentCode",
                "siteCode",
                "lineCode",
                "workCenterCode",
                "category",
                "partnerType",
                "keyword",
                "all",
                "departmentCode",
                "shiftCode",
                "userId",
                "skillCode",
                "workshopCode",
                "deviceAssetId",
            ],
            parameterNames);

        Assert.Equal(0, GetDefault(parameters, "skip"));
        Assert.Equal(100, GetDefault(parameters, "take"));
    }

    [Fact]
    public async Task OpenApi_document_freezes_all_three_master_data_list_contracts()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var document = await GetOpenApiDocumentAsync(client);
        var root = document.RootElement;
        var schemas = root.GetProperty("components").GetProperty("schemas");

        AssertListContract(
            root,
            schemas,
            "/api/business/v1/master-data/resources",
            "listBusinessMasterDataResources",
            ["organizationId", "environmentId", "resourceType", "includeDisabled", "skip", "take", "codeSet", "parentCode", "siteCode", "lineCode", "workCenterCode", "category", "partnerType", "keyword", "all", "departmentCode", "shiftCode", "userId", "skillCode", "workshopCode", "deviceAssetId"],
            "resources",
            ["limit", "resources", "total", "truncated"],
            ["resourceType", "code", "displayName", "active", "snapshotVersion", "partnerType", "partnerRoles", "siteCode", "plantCode", "lineCode", "workshopCode", "capacityMinutesPerDay", "workCenterCode", "status", "category", "materialType", "codeSet", "baseUomCode", "taxId", "parentDepartmentCode", "departmentCode", "shiftCode", "userId", "skillCode", "skillLevel", "effectiveFrom", "effectiveTo", "fromUomCode", "toUomCode", "factor", "offset", "precision", "roundingMode", "deviceAssetId", "purchaseDate", "purchaseCost", "purchaseCurrencyCode", "warrantyExpiresOn", "supplierPartnerCode", "stationCode", "parentDeviceId", "retiredOn", "creditLimit", "creditCurrencyCode", "jobTitle", "employmentStatus", "phone", "timezone", "startsAt", "endsAt", "crossesMidnight", "paidMinutes", "breakMinutes"]);
        AssertListContract(
            root,
            schemas,
            "/api/business/v1/master-data/product-categories",
            "listBusinessMasterDataProductCategories",
            ["organizationId", "environmentId", "enabled", "search", "parentCode", "skip", "take"],
            "items",
            ["items", "total"],
            ["categoryCode", "categoryName", "parentCode", "path", "description", "enabled", "snapshotVersion"]);
        AssertListContract(
            root,
            schemas,
            "/api/business/v1/master-data/skills",
            "listBusinessMasterDataSkills",
            ["organizationId", "environmentId", "enabled", "search", "groupName", "skip", "take"],
            "items",
            ["items", "total"],
            ["skillCode", "skillName", "groupName", "requiresCertification", "validityMonths", "description", "enabled", "snapshotVersion"]);
    }

    private static void AssertListContract(
        JsonElement root,
        JsonElement schemas,
        string path,
        string operationId,
        string[] parameterNames,
        string collectionPropertyName,
        string[] dataPropertyNames,
        string[] itemPropertyNames)
    {
        var operation = root.GetProperty("paths").GetProperty(path).GetProperty("get");
        var parameters = operation.GetProperty("parameters").EnumerateArray().ToArray();
        Assert.Equal(parameterNames, parameters.Select(parameter => parameter.GetProperty("name").GetString()));
        Assert.Equal(operationId, operation.GetProperty("operationId").GetString());
        Assert.Equal(0, GetDefault(parameters, "skip"));
        Assert.Equal(100, GetDefault(parameters, "take"));

        var responseSchema = ResolveSchema(
            operation.GetProperty("responses").GetProperty("200").GetProperty("content")
                .GetProperty("application/json").GetProperty("schema"),
            schemas);
        Assert.Equal(
            ["code", "data", "errorData", "message", "success"],
            GetSchemaPropertyNames(responseSchema, schemas));
        var dataProperty = GetSchemaProperty(responseSchema, "data", schemas);
        var dataSchema = ResolveSchema(dataProperty, schemas);
        Assert.Equal(dataPropertyNames.Order(StringComparer.Ordinal), GetSchemaPropertyNames(dataSchema, schemas));
        var collectionSchema = ResolveSchema(GetSchemaProperty(dataSchema, collectionPropertyName, schemas), schemas);
        var itemSchema = ResolveSchema(collectionSchema.GetProperty("items"), schemas);
        Assert.Equal(itemPropertyNames.Order(StringComparer.Ordinal), GetSchemaPropertyNames(itemSchema, schemas));
    }

    private static string[] GetSchemaPropertyNames(JsonElement schema, JsonElement schemas)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                names.Add(property.Name);
            }
        }

        if (schema.TryGetProperty("allOf", out var allOf))
        {
            foreach (var branch in allOf.EnumerateArray())
            {
                foreach (var propertyName in GetSchemaPropertyNames(ResolveSchema(branch, schemas), schemas))
                {
                    names.Add(propertyName);
                }
            }
        }

        return names.Order(StringComparer.Ordinal).ToArray();
    }

    private static JsonElement GetSchemaProperty(JsonElement schema, string propertyName, JsonElement schemas)
    {
        if (schema.TryGetProperty("properties", out var properties)
            && properties.TryGetProperty(propertyName, out var property))
        {
            return property;
        }

        if (schema.TryGetProperty("allOf", out var allOf))
        {
            foreach (var branch in allOf.EnumerateArray())
            {
                try
                {
                    return GetSchemaProperty(ResolveSchema(branch, schemas), propertyName, schemas);
                }
                catch (KeyNotFoundException)
                {
                    // Continue through the composed response branches.
                }
            }
        }

        throw new KeyNotFoundException($"Schema property '{propertyName}' was not found in {schema.GetRawText()}.");
    }

    private static int GetDefault(IEnumerable<JsonElement> parameters, string name) =>
        parameters
            .Single(parameter => parameter.GetProperty("name").GetString() == name)
            .GetProperty("schema")
            .GetProperty("default")
            .GetInt32();

    private static JsonElement ResolveSchema(JsonElement schema, JsonElement schemas)
    {
        if (schema.TryGetProperty("$ref", out var schemaReference))
        {
            return schemas.GetProperty(schemaReference.GetString()!.Split('/')[^1]);
        }

        if (schema.TryGetProperty("oneOf", out var alternatives))
        {
            return ResolveSchema(Assert.Single(alternatives.EnumerateArray()), schemas);
        }

        if (schema.TryGetProperty("allOf", out var inheritedSchemas))
        {
            return ResolveSchema(Assert.Single(inheritedSchemas.EnumerateArray()), schemas);
        }

        return schema;
    }

    private static void AssertStringToolingStatusSchema(JsonElement schema)
    {
        Assert.True(schema.TryGetProperty("type", out var type), $"枚举 schema 缺少 type：{schema.GetRawText()}");
        Assert.Equal("string", type.GetString());
        Assert.True(schema.TryGetProperty("enum", out var values), $"枚举 schema 缺少 enum：{schema.GetRawText()}");
        Assert.Equal(
            ["available", "maintenance", "retired"],
            values.EnumerateArray().Select(value => value.GetString()));
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Redis"] = "localhost:6379",
                        ["ConnectionStrings:PostgreSQL"] = "Host=localhost;Database=nerv_iip_masterdata_openapi;Username=nerv;Password=nerv",
                        ["InternalService:BearerToken"] = "test-internal-service-token",
                    }));
            });
    }

    private static async Task<JsonDocument> GetOpenApiDocumentAsync(HttpClient client)
    {
        await using var stream = await client.GetStreamAsync("/swagger/v1/swagger.json");
        return await JsonDocument.ParseAsync(stream);
    }

    private static void AssertOperationIdsAreUnique(JsonDocument document)
    {
        var operations = document.RootElement
            .GetProperty("paths")
            .EnumerateObject()
            .SelectMany(path => path.Value
                .EnumerateObject()
                .Where(operation => IsHttpMethod(operation.Name))
                .Select(operation => (
                    Name: $"{operation.Name.ToUpperInvariant()} {path.Name}",
                    OperationId: operation.Value.TryGetProperty("operationId", out var operationId)
                        ? operationId.GetString()
                        : null)))
            .ToArray();

        var missingOperationIds = operations
            .Where(operation => string.IsNullOrWhiteSpace(operation.OperationId))
            .Select(operation => operation.Name)
            .ToArray();
        Assert.Empty(missingOperationIds);

        var duplicateOperationIds = operations
            .Where(operation => !string.IsNullOrWhiteSpace(operation.OperationId))
            .GroupBy(operation => operation.OperationId!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(operation => operation.Name))}")
            .ToArray();
        Assert.Empty(duplicateOperationIds);
    }

    private static bool IsHttpMethod(string method) =>
        method is "get" or "post" or "put" or "patch" or "delete" or "head" or "options" or "trace";

    private static string GetOperationId(JsonDocument document, string route, string method)
    {
        var paths = document.RootElement.GetProperty("paths");
        if (!paths.TryGetProperty(route, out var path))
        {
            route = route
                .Replace("{ResourceType}", "{resourceType}", StringComparison.Ordinal)
                .Replace("{Code}", "{code}", StringComparison.Ordinal);
        }

        Assert.True(paths.TryGetProperty(route, out path), $"OpenAPI path '{route}' was not found.");
        Assert.True(path.TryGetProperty(method, out var operation), $"OpenAPI operation '{method} {route}' was not found.");
        return operation.GetProperty("operationId").GetString()!;
    }
}
