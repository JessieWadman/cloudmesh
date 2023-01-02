using CartServices;
using Proto;
using Proto.Cluster;
using System.Reflection;
using System.Runtime.InteropServices;

Console.WriteLine($"Starting up {Assembly.GetEntryAssembly().GetName().Name} on {RuntimeInformation.OSDescription} - {RuntimeInformation.OSArchitecture} - {RuntimeInformation.FrameworkDescription}");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddActorSystem();
builder.Services.AddHostedService<ActorSystemClusterHostedService>();
builder.Services.AddHostedService<VehicleSimulator>();

var app = builder.Build();

var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
Proto.Log.SetLoggerFactory(loggerFactory);

app.MapGet("/vehicles/{identity}", async (ActorSystem actorSystem, string identity) =>
{
    return await actorSystem
        .Cluster()
        .GetVehicleGrain(identity)
        .GetCurrentPosition(CancellationToken.None);
});

app.Run();