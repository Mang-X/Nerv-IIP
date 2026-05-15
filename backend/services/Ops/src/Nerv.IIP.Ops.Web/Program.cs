using FastEndpoints;
using Nerv.IIP.Observability;
using Nerv.IIP.Ops.Domain;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFastEndpoints();
builder.Services.AddSingleton<InMemoryOpsStateStore>();
builder.Services.AddNervIipObservability(builder.Configuration, "ops");

var app = builder.Build();
app.UseNervIipCorrelation();
app.UseFastEndpoints();
app.Run();

public partial class Program;
