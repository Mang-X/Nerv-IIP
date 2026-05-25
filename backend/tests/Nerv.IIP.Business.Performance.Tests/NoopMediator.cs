namespace Nerv.IIP.Business.Performance.Tests;

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
        throw new NotSupportedException("Performance baseline mediator cannot send requests.");
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        _ = request;
        _ = cancellationToken;
        throw new NotSupportedException("Performance baseline mediator cannot send requests.");
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        throw new NotSupportedException("Performance baseline mediator cannot send requests.");
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        throw new NotSupportedException("Performance baseline mediator cannot stream requests.");
    }

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        throw new NotSupportedException("Performance baseline mediator cannot stream requests.");
    }
}
