using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Nerv.IIP.Iam.Web.Tests;

internal sealed class NoopMediator : IMediator
{
    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        _ = notification;
        _ = cancellationToken;
        return Task.CompletedTask;
    }

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        _ = notification;
        _ = cancellationToken;
        return Task.CompletedTask;
    }

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        throw new NotSupportedException("Test mediator cannot send requests.");
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        _ = request;
        _ = cancellationToken;
        throw new NotSupportedException("Test mediator cannot send requests.");
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        throw new NotSupportedException("Test mediator cannot send requests.");
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        throw new NotSupportedException("Test mediator cannot create streams.");
    }

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        throw new NotSupportedException("Test mediator cannot create streams.");
    }
}

internal sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Nerv.IIP.Iam.Web.Tests";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = "Development";
    public string WebRootPath { get; set; } = string.Empty;
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
