using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityReinspectionPostgresProfileTests
{
    [QualityPostgresFact]
    public async Task Postgres_predecessor_conflict_reloads_the_committed_reinspection()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddQualityPostgreSqlPersistence(connectionString);
        await using var provider = services.BuildServiceProvider();

        InspectionRecordId predecessorId;
        using (var seedScope = provider.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await DropQualitySchemaAsync(seedDb);
            await seedDb.Database.MigrateAsync();
            var records = new InspectionRecordRepository(seedDb);
            predecessorId = await new CreateInspectionRecordCommandHandler(
                    records,
                    new InspectionPlanRepository(seedDb),
                    new InspectionTaskRepository(seedDb))
                .Handle(NewRejectedInspectionCommand(), CancellationToken.None);
            await seedDb.SaveChangesAsync();
        }

        using var winnerScope = provider.CreateScope();
        using var losingScope = provider.CreateScope();
        var winnerDb = winnerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var losingDb = losingScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var command = NewPassedReinspectionCommand(predecessorId);
        var winnerHandler = NewReinspectionHandler(winnerDb);
        var losingHandler = NewReinspectionHandler(losingDb);

        var winner = await winnerHandler.Handle(command, CancellationToken.None);
        var losingCandidate = await losingHandler.Handle(command, CancellationToken.None);
        Assert.NotEqual(winner.InspectionRecordId, losingCandidate.InspectionRecordId);
        await winnerDb.SaveChangesAsync();

        var behavior = new CreateReinspectionUniqueConflictBehavior(
            losingDb,
            new QualityPersistenceConflictClassifier());
        var attempt = 0;
        var converged = await behavior.Handle(
            command,
            async cancellationToken =>
            {
                attempt++;
                if (attempt == 1)
                {
                    await losingDb.SaveChangesAsync(cancellationToken);
                    return losingCandidate;
                }

                var replay = await NewReinspectionHandler(losingDb).Handle(command, cancellationToken);
                await losingDb.SaveChangesAsync(cancellationToken);
                return replay;
            },
            CancellationToken.None);

        Assert.Equal(2, attempt);
        Assert.Equal(winner, converged);
        await using var verificationScope = provider.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await verificationDb.InspectionRecords.CountAsync());
        Assert.Equal(
            winner.InspectionRecordId,
            await verificationDb.InspectionRecords
                .Where(record => record.ReinspectionOfInspectionRecordId == predecessorId)
                .Select(record => record.Id)
                .SingleAsync());
    }

    private static CreateReinspectionCommandHandler NewReinspectionHandler(ApplicationDbContext dbContext) =>
        new(
            new InspectionRecordRepository(dbContext),
            new InspectionPlanRepository(dbContext));

    private static CreateInspectionRecordCommand NewRejectedInspectionCommand() =>
        new(
            "org-001",
            "env-dev",
            null,
            "operation",
            "mes",
            "WO-REINSPECTION-PG-001",
            "SKU-FG-1000",
            5m,
            "LOT-PG-001",
            null,
            [new InspectionResultLineCommandInput(
                "appearance",
                "scratch",
                null,
                InspectionLineResults.Failed,
                "surface-defect",
                1m,
                [])],
            "Surface defect",
            []);

    private static CreateReinspectionCommand NewPassedReinspectionCommand(
        InspectionRecordId predecessorId) =>
        new(
            predecessorId,
            "org-001",
            "env-dev",
            [new InspectionResultLineCommandInput(
                "appearance",
                "ok",
                null,
                InspectionLineResults.Passed,
                null,
                null,
                [])],
            null,
            []);

    private static async Task DropQualitySchemaAsync(ApplicationDbContext dbContext)
    {
        await dbContext.Database.OpenConnectionAsync();
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS \"{QualityFacts.Schema}\" CASCADE";
        await command.ExecuteNonQueryAsync();
    }
}
