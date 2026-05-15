using FastEndpoints;
using Nerv.IIP.Caching;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFastEndpoints();
builder.Services.AddNervIipCaching(builder.Configuration, "iam");
builder.Services.AddNervIipObservability(builder.Configuration, "iam");
builder.Services.AddSingleton<InMemoryIamStore>();

var app = builder.Build();
app.UseNervIipCorrelation();
app.UseFastEndpoints();
app.Run();

public partial class Program;
