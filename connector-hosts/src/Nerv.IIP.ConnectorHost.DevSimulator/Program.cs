using Nerv.IIP.ConnectorHost.DevSimulator;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<SimulatedPlantState>();
builder.Services.AddHostedService(services => services.GetRequiredService<SimulatedPlantState>());
builder.Services.AddHostedService<ModbusTcpSlaveService>();
builder.Services.AddHostedService<MqttDevBrokerService>();
await builder.Build().RunAsync();
