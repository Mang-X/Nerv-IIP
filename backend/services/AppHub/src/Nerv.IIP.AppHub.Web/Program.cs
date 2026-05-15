using FastEndpoints;
using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.Caching;
using Nerv.IIP.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFastEndpoints();
builder.Services.AddNervIipCaching(builder.Configuration, "apphub");
builder.Services.AddNervIipObservability(builder.Configuration, "apphub");
builder.Services.AddSingleton<InMemoryAppHubStateStore>();

var app = builder.Build();
app.UseNervIipCorrelation();
app.UseFastEndpoints();
app.Run();

public partial class Program;
