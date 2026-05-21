using FastEndpoints;
using Nerv.IIP.Notification.Web.Application;
using NetCorePal.Extensions.AspNetCore;
using NetCorePal.Extensions.DependencyInjection;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFastEndpoints();
builder.Services.AddMediatR(configuration =>
{
    configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
    configuration.AddKnownExceptionValidationBehavior();
    configuration.AddUnitOfWorkBehaviors();
});

builder.Services.AddNotificationPersistence(builder.Configuration);

var app = builder.Build();
app.UseKnownExceptionHandler(_ => new() { KnownExceptionStatusCode = HttpStatusCode.BadRequest });
app.UseFastEndpoints();
app.Run();

public partial class Program;
