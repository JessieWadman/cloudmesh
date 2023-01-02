using Proto;
using Proto.Cluster;

namespace CartServices
{
    public record JourneyStats(int OnTime, int Late);

    public class DayReportGrain : DayReportGrainBase
    {
        private readonly ClusterIdentity clusterIdentity;
        private Dictionary<string, JourneyStats> stats = new();


        public DayReportGrain(IContext context, ClusterIdentity clusterIdentity)
            : base(context)
        {
            this.clusterIdentity = clusterIdentity;
        }

        public override Task UpdateDayReport(UpdateDayReportRequest request)
        {
            var journeyId = $"{request.VehicleNumber}.{request.LineNumber}.{request.JourneyNumber}";
            stats[journeyId] = new(request.PositionsOnTime, request.PositionsLate);

            Console.WriteLine($"{Context.Cluster().System.Address}: Stats updated for {clusterIdentity}: {request.PositionsOnTime} on time, {request.PositionsLate} late");
            decimal onTime = stats.Values.Select(v => v.OnTime).Sum();
            decimal notOnTime = stats.Values.Select(v => v.Late).Sum();
            decimal sla = 0;
            if (onTime + notOnTime != 0)
                sla = 100 * onTime / (onTime + notOnTime);
            Console.WriteLine($"{Context.Cluster().System.Address}: Day report {DateOnly.FromDayNumber(request.OperatingDay):yyyy-MM-dd} {onTime} on time, {notOnTime} late, {sla:n2} % SLA");

            return Task.CompletedTask;
        }

        public override Task OnReceive()
        {
            if (!Context.StopOnReceiveTimeout())
                return base.OnReceive();
            else
                return Task.CompletedTask;
        }
    }
}
