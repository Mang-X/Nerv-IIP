using FastEndpoints;
using Nerv.IIP.Caching;
using Nerv.IIP.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFastEndpoints();
builder.Services.AddNervIipCaching(builder.Configuration, "file-storage");
builder.Services.AddNervIipObservability(builder.Configuration, "file-storage");

var app = builder.Build();
app.UseNervIipCorrelation();
app.UseFastEndpoints();
app.Run();

public partial class Program;
