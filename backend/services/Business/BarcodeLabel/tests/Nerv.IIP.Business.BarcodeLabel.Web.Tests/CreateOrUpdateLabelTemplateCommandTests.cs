using MediatR;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.LabelTemplates;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class CreateOrUpdateLabelTemplateCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_template_status_is_mapped_to_a_chinese_known_exception(bool updateExisting)
    {
        await using var dbContext = CreateDbContext();
        if (updateExisting)
        {
            dbContext.LabelTemplates.Add(LabelTemplate.Create(
                "org-001", "env-dev", "tpl-a", "Template A", "file-a", "{}", "active"));
            await dbContext.SaveChangesAsync();
        }

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new CreateOrUpdateLabelTemplateCommandHandler(dbContext).Handle(
                new CreateOrUpdateLabelTemplateCommand(
                    "org-001", "env-dev", "tpl-a", "Template A", "file-a", "{}", "unsupported"),
                CancellationToken.None));

        Assert.Equal("标签模板参数无效，请检查后重试。", exception.Message);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
