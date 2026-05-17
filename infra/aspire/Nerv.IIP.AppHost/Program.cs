var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("nerv-iip-postgres");
var appHubDatabase = postgres.AddDatabase("apphub-db", "nerv_iip_apphub");
var opsDatabase = postgres.AddDatabase("ops-db", "nerv_iip_ops");
var redis = builder.AddRedis("redis")
    .WithDataVolume("nerv-iip-redis");
var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

var apphub = builder.AddProject<Projects.Nerv_IIP_AppHub_Web>("apphub")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithReference(appHubDatabase, "AppHubDb")
    .WithReference(redis)
    .WithReference(rabbitmq)
    .WaitFor(appHubDatabase)
    .WaitFor(redis)
    .WaitFor(rabbitmq);

var ops = builder.AddProject<Projects.Nerv_IIP_Ops_Web>("ops")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithReference(opsDatabase, "OpsDb")
    .WithReference(rabbitmq)
    .WaitFor(opsDatabase)
    .WaitFor(rabbitmq);

var gateway = builder.AddProject<Projects.Nerv_IIP_PlatformGateway_Web>("gateway")
    .WithReference(apphub)
    .WithReference(ops)
    .WithReference(redis)
    .WaitFor(apphub)
    .WaitFor(ops)
    .WaitFor(redis);

builder.AddProject<Projects.Nerv_IIP_ConnectorHost_Host>("connector-host")
    .WithEnvironment("ConnectorHost__CycleSeconds", "1")
    .WithReference(apphub)
    .WithReference(ops)
    .WaitFor(apphub)
    .WaitFor(ops);

builder.Build().Run();
