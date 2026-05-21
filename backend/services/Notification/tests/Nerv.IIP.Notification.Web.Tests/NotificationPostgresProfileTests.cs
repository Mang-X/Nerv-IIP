using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
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

        var databaseName = $"nerv_iip_notification_schema_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = databaseName,
        };

        await using var adminConnection = new NpgsqlConnection(connectionString);
        await adminConnection.OpenAsync();
        await using (var createCommand = adminConnection.CreateCommand())
        {
            createCommand.CommandText = $"""CREATE DATABASE "{databaseName}";""";
            await createCommand.ExecuteNonQueryAsync();
        }

        try
        {
            var services = new ServiceCollection();
            services.AddMediatR(configuration =>
            {
                configuration.RegisterServicesFromAssembly(typeof(NotificationIntent).Assembly);
            });
            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
                builder.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "notification")));

            await using var serviceProvider = services.BuildServiceProvider();
            await using var scope = serviceProvider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await db.Database.MigrateAsync();

            await using var connection = new NpgsqlConnection(builder.ConnectionString);
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
        finally
        {
            await using var cleanupConnection = new NpgsqlConnection(connectionString);
            await cleanupConnection.OpenAsync();
            await using var terminateCommand = cleanupConnection.CreateCommand();
            terminateCommand.CommandText = $"""
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = '{databaseName}';
                DROP DATABASE IF EXISTS "{databaseName}";
                """;
            await terminateCommand.ExecuteNonQueryAsync();
        }
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
