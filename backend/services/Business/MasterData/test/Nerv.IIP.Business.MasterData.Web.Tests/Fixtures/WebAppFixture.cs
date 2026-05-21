using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Microsoft.AspNetCore.Hosting;

namespace Nerv.IIP.Business.MasterData.Web.Tests.Fixtures;

public class WebAppFixture : AppFixture<Program>
{
    private RedisContainer _redisContainer = null!;
    private RabbitMqContainer _rabbitMqContainer = null!;
    private PostgreSqlContainer _databaseContainer = null!;

    protected override async ValueTask PreSetupAsync()
    {
        _redisContainer = new RedisBuilder()
            .WithCommand("--databases", "1024").Build();
        _rabbitMqContainer = new RabbitMqBuilder()
            .WithUsername("guest").WithPassword("guest").Build();
        _databaseContainer = new PostgreSqlBuilder()
            .WithUsername("postgres").WithPassword("123456")
            .WithEnvironment("TZ", "Asia/Shanghai")
            .WithDatabase("postgres").Build();

        var tasks = new List<Task> { _redisContainer.StartAsync() };
        tasks.Add(_rabbitMqContainer.StartAsync());
        tasks.Add(_databaseContainer.StartAsync());
        await Task.WhenAll(tasks);

        await CreateVisualHostAsync("/");
    }

    protected override void ConfigureApp(IWebHostBuilder a)
    {
        a.UseSetting("ConnectionStrings:Redis",
            _redisContainer.GetConnectionString());
        a.UseSetting("ConnectionStrings:PostgreSQL",
            _databaseContainer.GetConnectionString());
        a.UseSetting("RabbitMQ:Port", _rabbitMqContainer.GetMappedPublicPort(5672).ToString());
        a.UseSetting("RabbitMQ:UserName", "guest");
        a.UseSetting("RabbitMQ:Password", "guest");
        a.UseSetting("RabbitMQ:VirtualHost", "/");
        a.UseSetting("RabbitMQ:HostName", _rabbitMqContainer.Hostname);
        a.UseEnvironment("Development");
    }

    private async Task CreateVisualHostAsync(string visualHost)
    {
        await _rabbitMqContainer.ExecAsync(new string[] { "rabbitmqctl", "add_vhost", visualHost });
        await _rabbitMqContainer.ExecAsync(new string[]
            { "rabbitmqctl", "set_permissions", "-p", visualHost, "guest", ".*", ".*", ".*" });
    }
}