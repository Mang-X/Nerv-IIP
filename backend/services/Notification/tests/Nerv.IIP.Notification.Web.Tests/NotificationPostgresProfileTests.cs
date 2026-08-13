using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Testing.PostgreSql;
using Npgsql;

namespace Nerv.IIP.Notification.Web.Tests;

public sealed class NotificationPostgresProfileTests
{
    [Fact]
    public async Task PostgreSQL_profile_places_migrations_history_in_notification_schema_when_database_is_available()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__NotificationDb")
            ?? "Host=localhost;Port=15432;Database=nerv_iip_notification_test;Username=postgres;Password=postgres";

        if (!await CanConnectAsync(connectionString))
        {
            return;
        }

        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            connectionString,
            "nerv_notification_schema");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(NotificationIntent).Assembly);
        });
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
            database.ConnectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "notification")));

        await using var serviceProvider = services.BuildServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        database.AssertOwns(db.Database.GetConnectionString());

        await db.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = 'notification'
                  AND table_name = '__EFMigrationsHistory'
            );
            """;
        var exists = (bool)(await command.ExecuteScalarAsync() ?? false);
        Assert.True(exists, "Notification migrations history table must be created in notification.__EFMigrationsHistory.");
    }

    private static async Task<bool> CanConnectAsync(string connectionString)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
