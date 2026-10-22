using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Infrastructure.Records;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;
using Nerv.IIP.FileStorage.Web.Application.Files.UploadProviders;
using Nerv.IIP.ServiceAuth;
using FileStorageFileStatus = Nerv.IIP.FileStorage.Domain.FileStorageFileStatus;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed partial class FileStorageTusProviderTests
{
    [Fact]
    public async Task TusUploadEndpoint_ChecksumTrailer_IsRejectedWithoutAdvancingOffset()
    {
        var root = CreateTempDirectory();
        try
        {
            await using var factory = CreateFactoryWithTusProvider(root).WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(new ChecksumTrailerFilter())));
            using var client = CreateInternalServiceClient(factory);
            var created = await CreateTusUploadSessionAsync(client, expectedSizeBytes: 10);
            using var request = CreateTusPatchRequest(created.Upload.Url, 0, Encoding.UTF8.GetBytes("hello"));
            request.Headers.Add("Trailer", "Upload-Checksum");
            var response = await client.SendAsync(request);
            var head = await SendTusHeadAsync(client, created.Upload.Url);
            Assert.Equal(StatusCodes.Status400BadRequest, (int)response.StatusCode);
            Assert.Equal(0, GetUploadOffset(head));
            Assert.Equal(0, CreateTusStore(root).GetOffset(created.UploadSessionId));
            using var options = new HttpRequestMessage(HttpMethod.Options, "/api/files/v1/tus");
            var capabilities = await client.SendAsync(options);
            Assert.DoesNotContain("checksum-trailer", Assert.Single(capabilities.Headers.GetValues("Tus-Extension")).Split(','));
        }
        finally { DeleteTempDirectory(root); }
    }

    private sealed class ChecksumTrailerFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            Microsoft.AspNetCore.Builder.UseExtensions.Use(app, async (context, following) =>
            {
                if (context.Request.Method == "PATCH")
                    context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpRequestTrailersFeature>(new ChecksumTrailers());
                await following(context);
            });
            next(app);
        };
    }

    private sealed class ChecksumTrailers : Microsoft.AspNetCore.Http.Features.IHttpRequestTrailersFeature
    {
        public bool Available => true;
        public IHeaderDictionary Trailers { get; } = new HeaderDictionary
        {
            ["Upload-Checksum"] = $"sha256 {Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("other")))}"
        };
    }

}
