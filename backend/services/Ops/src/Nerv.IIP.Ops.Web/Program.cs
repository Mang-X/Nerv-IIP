using Nerv.IIP.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNervIipObservability(builder.Configuration, "ops");

var app = builder.Build();
app.UseNervIipCorrelation();

app.MapGet("/health", () => "Healthy");
app.MapGet("/internal/ops/v1/build-info", () => "Ops first-iteration-skeleton");

app.Run();

public partial class Program;
