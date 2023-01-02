using Proto;
using Proto.Cluster;
using static Proto.CancellationTokens;

namespace CartServices;

public class VehicleRouteGrain : VehicleRouteGrainBase
{
    private readonly ClusterIdentity _clusterIdentity;

    private DateTimeOffset? signedOn;
    private DateTimeOffset? signedOff;

    private int vehicleNumber;
    private int lineNumber;
    private int journeyNumber;

    private readonly SortedSet<long> positionsOnTime = new();
    private readonly SortedSet<long> positionsNotOnTime = new();

    private bool pendingChanges = false;

    public VehicleRouteGrain(IContext context, ClusterIdentity clusterIdentity) : base(context)
    {
        _clusterIdentity = clusterIdentity;
        Console.WriteLine($"{Context.Cluster().System.Address}: {_clusterIdentity.Identity} created");
        Context.SetReceiveTimeout(TimeSpan.FromMinutes(10));
    }

    public override Task SignOn(VehicleSignOnRequest request)
    {
        this.signedOn = request.ReportTimestamp.ToDateTimeOffset();
        this.pendingChanges = true;

        if (this.vehicleNumber == 0) this.vehicleNumber = request.VehicleNumber;
        if (this.lineNumber == 0) this.lineNumber = request.LineNumber;
        if (this.journeyNumber == 0) this.journeyNumber = request.JourneyNumber;

        return Task.CompletedTask;
    }

    public override Task SignOff(VehicleSignOffRequest request)
    {
        this.signedOff = request.ReportTimestamp.ToDateTimeOffset();
        this.pendingChanges = true;

        if (this.vehicleNumber == 0) this.vehicleNumber = request.VehicleNumber;
        if (this.lineNumber == 0) this.lineNumber = request.LineNumber;
        if (this.journeyNumber == 0) this.journeyNumber = request.JourneyNumber;

        Context.SetReceiveTimeout(TimeSpan.FromSeconds(10));

        return Task.CompletedTask;
    }

    private async Task UpdateDayReport()
    {
        if (!this.signedOn.HasValue)
        {
            Console.WriteLine($"{Context.Cluster().System.Address}: Unable to update day report with route {_clusterIdentity}, because no sign-on was registered");
            return;
        }

        var operatingDate = DateOnly.FromDateTime(this.signedOn.Value.Date);
        var operatingDay = operatingDate.ToString("yyyy-MM-dd");

        if (this.signedOn.HasValue)
        {
            await Context.GetDayReportGrain(operatingDay)
                .UpdateDayReport(new()
                {
                    OperatingDay = operatingDate.DayNumber,
                    VehicleNumber = this.vehicleNumber,
                    LineNumber = this.lineNumber,
                    JourneyNumber = this.journeyNumber,
                    PositionsLate = this.positionsNotOnTime.Count(),
                    PositionsOnTime = this.positionsOnTime.Count()
                }, FromSeconds(10));
        }
    }

    public override async Task OnPositionDelivered(PositionDeliveredRequest request)
    {
        var messageIdHash = MurmurHash2.Hash(request.MessageId);

        Console.WriteLine($"{Context.Cluster().System.Address}: Message [{request.MessageId}] was {(request.OnTime ? "on time" : "late")}");

        var shouldAdd = true;

        if (this.positionsOnTime.Contains(messageIdHash))
        {
            if (!request.OnTime)
                this.positionsOnTime.Remove(messageIdHash);
            else
                shouldAdd = false;
        }
        else if (this.positionsNotOnTime.Contains(messageIdHash))
        {
            if (request.OnTime)
                this.positionsNotOnTime.Remove(messageIdHash);
            else
                shouldAdd = false;
        }

        if (shouldAdd && request.OnTime)
            positionsOnTime.Add(messageIdHash);
        else if (shouldAdd)
            positionsNotOnTime.Add(messageIdHash);

        if (this.signedOn.HasValue && this.signedOff.HasValue)
            await UpdateDayReport();

        if (shouldAdd)
            this.pendingChanges = true;
    }

    public override Task OnReceive()
    {
        if (!Context.StopOnReceiveTimeout())
            return base.OnReceive();
        else
            return Task.CompletedTask;
    }

    public override async Task OnStopping()
    {
        if (pendingChanges)
        {
            await UpdateDayReport();
            Console.WriteLine($"{Context.Cluster().System.Address}: Route {_clusterIdentity} stopped and flushed");
        }
        else
            Console.WriteLine($"{Context.Cluster().System.Address}: Route {_clusterIdentity} stopped with nothing to flush");
    }
}