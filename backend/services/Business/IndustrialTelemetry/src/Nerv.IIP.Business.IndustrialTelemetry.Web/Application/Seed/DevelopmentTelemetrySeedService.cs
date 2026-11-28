using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Contracts.Notification;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Seed;

public sealed class DevelopmentTelemetrySeedService(
    ApplicationDbContext dbContext, ITelemetryDeviceReferences references, ISender sender, IConfiguration configuration)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        var org = configuration["IndustrialTelemetry:Seed:OrganizationId"] ?? "org-001";
        var env = configuration["IndustrialTelemetry:Seed:EnvironmentId"] ?? "env-dev";
        var cnc = (await references.ResolveAsync(org, env, "DEV-CNC-01", ct)).PublicId;
        var aux = (await references.ResolveAsync(org, env, "DEV-AUX-01", ct)).PublicId;
        var assembly = (await references.ResolveAsync(org, env, "DEV-ASM-01", ct)).PublicId;
        var coating = (await references.ResolveAsync(org, env, "DEV-CTG-01", ct)).PublicId;
        var test = (await references.ResolveAsync(org, env, "DEV-TST-01", ct)).PublicId;
        await AddConnectorAsync(new("dev-modbus-workshop", "机加车间 · Modbus TCP", "modbus",
            configuration["DevSimulator:ModbusEndpoint"] ?? "tcp://127.0.0.1:15020", true, 2,
            [
                new(cnc, "spindle-temperature", "°C"),
                new(cnc, "spindle-speed", "rpm", Address: 40003),
                new(cnc, "vibration", "mm/s", Address: 40005),
                new(cnc, "parts-count", "pcs", Address: 40007, DataType: "UInt16"),
                new(aux, "air-pressure", "bar", UnitId: 2),
                new(aux, "temperature", "°C", UnitId: 2, Address: 40003),
                new(aux, "motor-current", "A", UnitId: 2, Address: 40005),
            ]));
        await AddConnectorAsync(new("dev-mqtt-assembly", "前减装配 · MQTT", "mqtt",
            configuration["DevSimulator:MqttEndpoint"] ?? "tcp://127.0.0.1:11883", true, 2,
            [
                new(assembly, "press-force", "kN", TopicFilter: "factory/DEV-ASM-01/telemetry", ValueJsonPath: "$.pressForce"),
                new(assembly, "cycle-count", "pcs", TopicFilter: "factory/DEV-ASM-01/telemetry", ValueJsonPath: "$.cycleCount"),
            ]));

        await AddAlarmAsync(cnc, "spindle-temperature", ">", 72m, "°C", NotificationContractConstants.SeverityCritical);
        await AddAlarmAsync(aux, "air-pressure", "<", 6m, "bar", NotificationContractConstants.SeverityWarning);
        await AddAlarmAsync(assembly, "press-force", ">", 17m, "kN", NotificationContractConstants.SeverityWarning);

        var localToday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).Date);
        (string Id, string WorkCenter, string Line, string Workshop, int Quantity)[] devices =
        [
            (cnc, "WC-ROD-01", "LINE-WB-ROD-01", "WS-01", 420),
            (aux, "WC-AUX-MC", "LINE-WB-GRD-01", "WS-01", 0),
            (coating, "WC-CT-01", "LINE-WB-CT-01", "WS-03", 385),
            (assembly, "WC-FA-01", "LINE-WB-FA-01", "WS-02", 450),
            (test, "WC-TS-01", "LINE-WB-TS-01", "WS-03", 410)
        ];
        foreach (var (id, center, line, workshop, quantity) in devices)
        {
            // The seven-day OEE window includes today and needs a state before its opening boundary.
            for (var daysAgo = 7; daysAgo >= 0; daysAgo--)
            {
                var date = localToday.AddDays(-daysAgo);
                var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                var reportNo = $"dev-sim:{center}:{date:yyyyMMdd}";
                await AddStateAsync(id, EquipmentRuntimeDeviceStates.Running, start, $"{reportNo}:start");
                await AddStateAsync(id, EquipmentRuntimeDeviceStates.Stopped, start.AddHours(7.5), $"{reportNo}:stop");
                if (quantity > 0 && !await dbContext.OeeProductionFacts.AnyAsync(x =>
                    x.OrganizationId == org && x.EnvironmentId == env && x.SourceReportNo == reportNo, ct))
                {
                    var dimension = new OeeHistoricalDimensionSnapshot("SITE-001", workshop, line, "EARLY", "Asia/Shanghai",
                        new TimeOnly(8, 0), new TimeOnly(16, 0), false, 480, 0, date, start, start.AddHours(8), OeeHistoricalDimensionStatus.Resolved);
                    dbContext.OeeProductionFacts.Add(OeeProductionFact.Project(org, env, reportNo, center, id,
                        quantity - 12 * daysAgo, 4 + daysAgo, 0, "pcs", 65, start.AddHours(7), dimension));
                }
            }
        }
        await dbContext.SaveChangesAsync(ct);

        async Task AddConnectorAsync(TelemetryConnectorConfigInput config)
        {
            if (!await dbContext.TelemetryConnectorConfigs.AnyAsync(x => x.OrganizationId == org
                && x.EnvironmentId == env && x.ConnectorId == config.ConnectorId, ct))
            {
                await sender.Send(new SaveTelemetryConnectorConfigCommand(org, env, config), ct);
            }
        }

        async Task AddAlarmAsync(string device, string tag, string comparison, decimal threshold, string unit, string severity)
        {
            var code = $"dev-{tag}";
            if (!await dbContext.AlarmRules.AnyAsync(x => x.OrganizationId == org && x.EnvironmentId == env
                && x.DeviceAssetId == device && x.RuleCode == code, ct))
            {
                dbContext.AlarmRules.Add(AlarmRule.Configure(org, env, device, code, code, severity, tag, comparison, threshold, unit, true));
            }
        }

        async Task AddStateAsync(string device, string state, DateTimeOffset occurredAt, string sequence)
        {
            if (!await dbContext.DeviceStateSnapshots.AnyAsync(x => x.OrganizationId == org && x.EnvironmentId == env
                && x.DeviceAssetId == device && x.SourceSequence == sequence, ct))
            {
                dbContext.DeviceStateSnapshots.Add(DeviceStateSnapshot.Record(org, env, device, state, occurredAt,
                    sequence, "dev-seed", raiseChangedEvent: false));
            }
        }
    }
}
