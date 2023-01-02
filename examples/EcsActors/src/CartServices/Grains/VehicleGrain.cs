using Proto;
using Proto.Cluster;
using static Proto.CancellationTokens;

namespace CartServices;

public record Position(long Timestamp, double Latitude, double Longitude, double Heading, double Speed);

public class VehicleGrain : VehicleGrainBase
{
    private readonly ClusterIdentity clusterIdentity;

    private double longitude;
    private double latitude;
    private double speed;
    private double heading;

    public VehicleGrain(IContext context, ClusterIdentity clusterIdentity) : base(context)
    {
        this.clusterIdentity = clusterIdentity;
        Console.WriteLine($"{Context.Cluster().System.Address}: Vehicle {this.clusterIdentity.Identity}: created");
        Context.SetReceiveTimeout(TimeSpan.FromDays(4));
    }

    public override async Task SignOn(VehicleSignOnRequest request)
    {
        Console.WriteLine($"{Context.Cluster().System.Address}: Vehicle {clusterIdentity} signed onto route {request.LineNumber}/{request.JourneyNumber}");

        await Context
            .GetVehicleRouteGrain($"{request.VehicleNumber}-{request.LineNumber}-{request.JourneyNumber}")
            .SignOn(request, FromSeconds(10));
    }

    public override async Task SignOff(VehicleSignOffRequest request)
    {
        Console.WriteLine($"{Context.Cluster().System.Address}: Vehicle {clusterIdentity} signed off from route {request.LineNumber}/{request.JourneyNumber}");

        await Context
            .GetVehicleRouteGrain($"{request.VehicleNumber}-{request.LineNumber}-{request.JourneyNumber}")
            .SignOff(request, FromSeconds(10));
    }

    public override async Task UpdatePosition(VehicleUpdatePositionRequest request)
    {
        Console.WriteLine($"{Context.Cluster().System.Address}: Vehicle {clusterIdentity} set position: {request.Latitude:n4} {request.Longitude:n4}");

        this.latitude = request.Latitude;
        this.longitude = request.Longitude;        
        this.heading = request.Heading;
        this.speed = request.Speed;

        var onTime = request.ReceivedTimestamp.ToDateTimeOffset() - request.ReportTimestamp.ToDateTimeOffset() <= TimeSpan.FromSeconds(2);

        await Context
            .GetVehicleRouteGrain($"{request.VehicleNumber}-{request.LineNumber}-{request.JourneyNumber}")
            .OnPositionDelivered(new() { MessageId = request.MessageId, OnTime = onTime }, FromSeconds(10));
    }

    public override Task<GetVehicleCurrentPositionResponse> GetCurrentPosition()
        => Task.FromResult(new GetVehicleCurrentPositionResponse
        {  
            Latitude = this.latitude,
            Longitude = this.longitude,
            Heading = this.heading,
            Speed = this.speed
        });

    public override Task OnReceive()
    {
        if (!Context.StopOnReceiveTimeout())
            return base.OnReceive();
        else
            return Task.CompletedTask;
    }
}