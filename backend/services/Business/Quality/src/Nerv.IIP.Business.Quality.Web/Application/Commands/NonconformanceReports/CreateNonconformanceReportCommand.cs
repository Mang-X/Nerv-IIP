using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;

public sealed record CreateNonconformanceReportResult(NonconformanceReportId NcrId, string NcrCode);

public sealed record CreateNonconformanceReportCommand(
    string OrganizationId,
    string EnvironmentId,
    string SourceType,
    string SourceDocumentId,
    string SkuCode,
    decimal DefectQuantity,
    string DefectReason,
    string? BatchNo,
    string? SerialNo,
    IReadOnlyCollection<string> AttachmentFileIds) : ICommand<CreateNonconformanceReportResult>;

public sealed class CreateNonconformanceReportCommandValidator : AbstractValidator<CreateNonconformanceReportCommand>
{
    public CreateNonconformanceReportCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SourceDocumentId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DefectQuantity).GreaterThan(0);
        RuleFor(x => x.DefectReason).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreateNonconformanceReportCommandHandler(
    INonconformanceReportRepository repository,
    INonconformanceReportCodeGenerator codeGenerator)
    : ICommandHandler<CreateNonconformanceReportCommand, CreateNonconformanceReportResult>
{
    public async Task<CreateNonconformanceReportResult> Handle(CreateNonconformanceReportCommand request, CancellationToken cancellationToken)
    {
        var ncrCode = await codeGenerator.NextAsync(request.OrganizationId, request.EnvironmentId, cancellationToken);
        if (await repository.CodeExistsAsync(request.OrganizationId, request.EnvironmentId, ncrCode, cancellationToken))
        {
            throw new KnownException($"NCR code '{ncrCode}' already exists.");
        }

        var ncr = NonconformanceReport.Open(
            request.OrganizationId,
            request.EnvironmentId,
            ncrCode,
            request.SourceType,
            request.SourceDocumentId,
            request.SkuCode,
            request.DefectQuantity,
            request.DefectReason,
            request.BatchNo,
            request.SerialNo,
            request.AttachmentFileIds);
        await repository.AddAsync(ncr, cancellationToken);
        return new CreateNonconformanceReportResult(ncr.Id, ncr.NcrCode);
    }
}

public interface INonconformanceReportCodeGenerator
{
    Task<string> NextAsync(string organizationId, string environmentId, CancellationToken cancellationToken);
}

public sealed class NonconformanceReportCodeGenerator : INonconformanceReportCodeGenerator
{
    public Task<string> NextAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        _ = organizationId;
        _ = environmentId;
        cancellationToken.ThrowIfCancellationRequested();

        var code = $"NCR-{Guid.CreateVersion7():N}";
        return Task.FromResult(code);
    }
}
