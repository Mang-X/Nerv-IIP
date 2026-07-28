using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.MeasuringDeviceAggregate;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries.MeasuringDevices;

/// <summary>计量器具台账一行（含按 <paramref name="CalibrationState"/> 折算出的校准有效性）。</summary>
public sealed record MeasuringDeviceResponse(
    MeasuringDeviceId MeasuringDeviceId,
    string OrganizationId,
    string EnvironmentId,
    string DeviceCode,
    string DeviceType,
    string Accuracy,
    int CalibrationIntervalDays,
    string Status,
    DateTimeOffset? LastCalibratedAtUtc,
    DateTimeOffset CalibrationDueAtUtc,
    string CalibrationState,
    int DaysUntilDue,
    int CalibrationRecordCount,
    string? LatestCalibrationNo,
    string? LatestCalibrationProvider);

public sealed record ListMeasuringDevicesResponse(
    IReadOnlyCollection<MeasuringDeviceResponse> Items,
    int Total,
    int CurrentCount,
    int WarningCount,
    int OverdueCount,
    int UnavailableCount);

/// <summary>
/// 计量器具台账读面。
///
/// 与既有的 <c>GetCalibrationDashboardEndpoint</c> 的分工：仪表盘一次性返回全部器具用于统计卡片，
/// 本查询是可过滤、可分页的台账，且回带每台器具的校准次数与末次校准证书号——
/// 「计量失效风险」这条演示线要在一张表里同时讲清「谁过期了 / 上次谁校的 / 证书号是多少」。
/// </summary>
public sealed record ListMeasuringDevicesQuery(
    string OrganizationId,
    string EnvironmentId,
    string? DeviceType = null,
    string? Status = null,
    string? CalibrationState = null,
    string? Keyword = null,
    int WarningDays = 7,
    int Skip = 0,
    int Take = 100) : IQuery<ListMeasuringDevicesResponse>;

public sealed class ListMeasuringDevicesQueryValidator : AbstractValidator<ListMeasuringDevicesQuery>
{
    public ListMeasuringDevicesQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceType).MaximumLength(100);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.CalibrationState).MaximumLength(50);
        RuleFor(x => x.Keyword).MaximumLength(200);
        RuleFor(x => x.WarningDays).InclusiveBetween(0, 365);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class ListMeasuringDevicesQueryHandler(ApplicationDbContext dbContext, TimeProvider timeProvider)
    : IQueryHandler<ListMeasuringDevicesQuery, ListMeasuringDevicesResponse>
{
    public async Task<ListMeasuringDevicesResponse> Handle(
        ListMeasuringDevicesQuery request,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var warningDays = Math.Clamp(request.WarningDays, 0, 365);
        var warningBoundary = now.AddDays(warningDays);
        var take = Math.Clamp(request.Take, 1, 500);
        const string retired = MeasuringDeviceStatuses.Retired;
        const string disabled = MeasuringDeviceStatuses.Disabled;

        var baseQuery = dbContext.MeasuringDevices
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.DeviceType))
        {
            var deviceType = request.DeviceType.Trim();
            baseQuery = baseQuery.Where(x => x.DeviceType == deviceType);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim();
            baseQuery = baseQuery.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            baseQuery = baseQuery.Where(x =>
                x.DeviceCode.ToLower().Contains(keyword)
                || x.DeviceType.ToLower().Contains(keyword)
                || x.Accuracy.ToLower().Contains(keyword));
        }

        // 统计卡片按「过滤后但未按校准状态收窄」的集合算，切换状态页签时四个数字保持不变。
        var unavailableCount = await baseQuery.CountAsync(
            x => x.Status == retired || x.Status == disabled, cancellationToken);
        var overdueCount = await baseQuery.CountAsync(
            x => x.Status != retired && x.Status != disabled && x.CalibrationDueAtUtc < now, cancellationToken);
        var warningCount = await baseQuery.CountAsync(
            x => x.Status != retired && x.Status != disabled
                && x.CalibrationDueAtUtc >= now && x.CalibrationDueAtUtc <= warningBoundary,
            cancellationToken);
        var currentCount = await baseQuery.CountAsync(
            x => x.Status != retired && x.Status != disabled && x.CalibrationDueAtUtc > warningBoundary,
            cancellationToken);

        // 状态谓词与 MeasuringDevice.ComputeCalibrationState 同口径（停用/报废先短路，再比到期时刻）。
        var filtered = request.CalibrationState?.Trim().ToLowerInvariant() switch
        {
            MeasuringDeviceCalibrationStates.Unavailable =>
                baseQuery.Where(x => x.Status == retired || x.Status == disabled),
            MeasuringDeviceCalibrationStates.Overdue =>
                baseQuery.Where(x => x.Status != retired && x.Status != disabled && x.CalibrationDueAtUtc < now),
            MeasuringDeviceCalibrationStates.Warning =>
                baseQuery.Where(x => x.Status != retired && x.Status != disabled
                    && x.CalibrationDueAtUtc >= now && x.CalibrationDueAtUtc <= warningBoundary),
            MeasuringDeviceCalibrationStates.Current =>
                baseQuery.Where(x => x.Status != retired && x.Status != disabled
                    && x.CalibrationDueAtUtc > warningBoundary),
            _ => baseQuery,
        };

        var total = await filtered.CountAsync(cancellationToken);
        var rows = await filtered
            // 最快过期的排最前——台账页打开就是「该去校准的先看」。次序键用业务码而不是强类型 id。
            .OrderBy(x => x.CalibrationDueAtUtc)
            .ThenBy(x => x.DeviceCode)
            .Skip(request.Skip)
            .Take(take)
            .Select(x => new
            {
                Device = x,
                RecordCount = x.CalibrationRecords.Count,
                LatestCalibrationNo = x.CalibrationRecords
                    .OrderByDescending(record => record.CalibratedAtUtc)
                    .Select(record => record.CalibrationNo)
                    .FirstOrDefault(),
                LatestCalibrationProvider = x.CalibrationRecords
                    .OrderByDescending(record => record.CalibratedAtUtc)
                    .Select(record => record.CalibrationProvider)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new MeasuringDeviceResponse(
                row.Device.Id,
                row.Device.OrganizationId,
                row.Device.EnvironmentId,
                row.Device.DeviceCode,
                row.Device.DeviceType,
                row.Device.Accuracy,
                row.Device.CalibrationIntervalDays,
                row.Device.Status,
                row.Device.LastCalibratedAtUtc,
                row.Device.CalibrationDueAtUtc,
                row.Device.ComputeCalibrationState(now, warningDays),
                (int)Math.Floor((row.Device.CalibrationDueAtUtc - now).TotalDays),
                row.RecordCount,
                row.LatestCalibrationNo,
                row.LatestCalibrationProvider))
            .ToArray();

        return new ListMeasuringDevicesResponse(
            items,
            total,
            currentCount,
            warningCount,
            overdueCount,
            unavailableCount);
    }
}

/// <summary>一条校准记录（回带所属器具的编码/类型，读面不需要前端再拉一次台账）。</summary>
public sealed record CalibrationRecordResponse(
    CalibrationRecordId CalibrationRecordId,
    MeasuringDeviceId MeasuringDeviceId,
    string DeviceCode,
    string DeviceType,
    string CalibrationNo,
    DateTimeOffset CalibratedAtUtc,
    string CalibrationProvider,
    string? CertificateFileId,
    int CalibrationIntervalDays,
    DateTimeOffset NextCalibrationDueAtUtc);

public sealed record ListCalibrationRecordsResponse(
    IReadOnlyCollection<CalibrationRecordResponse> Items,
    int Total);

/// <summary>校准记录流水读面：按器具或时间窗回溯「谁校的、证书号、下次到期」。</summary>
public sealed record ListCalibrationRecordsQuery(
    string OrganizationId,
    string EnvironmentId,
    MeasuringDeviceId? MeasuringDeviceId = null,
    string? Keyword = null,
    DateTimeOffset? CalibratedFromUtc = null,
    DateTimeOffset? CalibratedToUtc = null,
    int Skip = 0,
    int Take = 100) : IQuery<ListCalibrationRecordsResponse>;

public sealed class ListCalibrationRecordsQueryValidator : AbstractValidator<ListCalibrationRecordsQuery>
{
    public ListCalibrationRecordsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Keyword).MaximumLength(200);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class ListCalibrationRecordsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListCalibrationRecordsQuery, ListCalibrationRecordsResponse>
{
    public async Task<ListCalibrationRecordsResponse> Handle(
        ListCalibrationRecordsQuery request,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(request.Take, 1, 500);

        // 筛选与排序必须停留在实体（或匿名类型）上：positional record 的构造是 Members == null 的
        // NewExpression，EF Core 无法把 `new Projection(...).Prop` 归约回列访问，一旦在其之上再拼
        // Where/OrderBy 就会翻译失败。投影因此放在 Skip/Take 之后（与本文件
        // ListMeasuringDevicesQuery、SpcAnalysisQueries 的写法一致）。
        var query =
            from record in dbContext.CalibrationRecords.AsNoTracking()
            join device in dbContext.MeasuringDevices.AsNoTracking() on record.MeasuringDeviceId equals device.Id
            where device.OrganizationId == request.OrganizationId
                && device.EnvironmentId == request.EnvironmentId
            select new { record, device };

        if (request.MeasuringDeviceId is { } measuringDeviceId)
        {
            query = query.Where(x => x.device.Id == measuringDeviceId);
        }

        if (request.CalibratedFromUtc is { } from)
        {
            query = query.Where(x => x.record.CalibratedAtUtc >= from);
        }

        if (request.CalibratedToUtc is { } to)
        {
            query = query.Where(x => x.record.CalibratedAtUtc <= to);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            query = query.Where(x =>
                x.record.CalibrationNo.ToLower().Contains(keyword)
                || x.device.DeviceCode.ToLower().Contains(keyword)
                || x.record.CalibrationProvider.ToLower().Contains(keyword));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.record.CalibratedAtUtc)
            .ThenBy(x => x.record.CalibrationNo)
            .Skip(request.Skip)
            .Take(take)
            .Select(x => new CalibrationRecordProjection(
                x.record.Id,
                x.device.Id,
                x.device.DeviceCode,
                x.device.DeviceType,
                x.device.CalibrationIntervalDays,
                x.record.CalibrationNo,
                x.record.CalibratedAtUtc,
                x.record.CalibrationProvider,
                x.record.CertificateFileId))
            .ToListAsync(cancellationToken);

        // 下次到期 = 本次校准时刻 + 器具校准周期：逐 provider 都能翻译的加法留在内存里做。
        var items = rows
            .Select(row => new CalibrationRecordResponse(
                row.CalibrationRecordId,
                row.MeasuringDeviceId,
                row.DeviceCode,
                row.DeviceType,
                row.CalibrationNo,
                row.CalibratedAtUtc,
                row.CalibrationProvider,
                row.CertificateFileId,
                row.CalibrationIntervalDays,
                row.CalibratedAtUtc.AddDays(row.CalibrationIntervalDays)))
            .ToArray();

        return new ListCalibrationRecordsResponse(items, total);
    }

    private sealed record CalibrationRecordProjection(
        CalibrationRecordId CalibrationRecordId,
        MeasuringDeviceId MeasuringDeviceId,
        string DeviceCode,
        string DeviceType,
        int CalibrationIntervalDays,
        string CalibrationNo,
        DateTimeOffset CalibratedAtUtc,
        string CalibrationProvider,
        string? CertificateFileId);
}
