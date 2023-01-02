using Google.Protobuf.WellKnownTypes;
using Proto;
using Proto.Cluster;

namespace CartServices;

public class VehicleSimulator : BackgroundService
{
    private readonly ActorSystem _actorSystem;

    public VehicleSimulator(ActorSystem actorSystem)
    {
        _actorSystem = actorSystem;
    }

    private async Task SignOn(int vehicleNo, int journeyNo)
    {
        await _actorSystem
                .Cluster()
                .GetVehicleGrain(vehicleNo.ToString())
                .SignOn(new()
                {
                    VehicleNumber = vehicleNo,
                    LineNumber = 1,
                    JourneyNumber = journeyNo,
                    MessageId = "sign_on_1",
                    ReportTimestamp = DateTimeOffset.Now.ToTimestamp(),
                    ReceivedTimestamp = DateTimeOffset.Now.ToTimestamp()
                }, default);
    }

    private async Task SignOff(int vehicleNo, int journeyNo)
    {
        await _actorSystem
                .Cluster()
                .GetVehicleGrain(vehicleNo.ToString())
                .SignOff(new()
                {
                    VehicleNumber = vehicleNo,
                    LineNumber = 1,
                    JourneyNumber = journeyNo,
                    MessageId = "sign_off_1",
                    ReportTimestamp = DateTimeOffset.Now.ToTimestamp(),
                    ReceivedTimestamp = DateTimeOffset.Now.ToTimestamp()
                }, default);
    }

    private async Task SetPosition(string messageId, int vehicleNo, int journeyNo, int latency)
    {
        await _actorSystem
                .Cluster()
                .GetVehicleGrain(vehicleNo.ToString())
                .UpdatePosition(new()
                {
                    MessageId = messageId,
                    VehicleNumber = vehicleNo,
                    LineNumber = 1,
                    JourneyNumber = journeyNo,

                    Latitude = 14,
                    Longitude = 13,
                    Heading = 16,
                    Speed = 17,
                    ReportTimestamp = DateTimeOffset.Now.AddMilliseconds(-latency).ToTimestamp(),
                    ReceivedTimestamp = DateTimeOffset.Now.ToTimestamp(),
                }, default);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(1000);

        var simulators = Enumerable.Range(1, 2).Select(vehicleNo => SimulateVehicle(vehicleNo, stoppingToken));
        await Task.WhenAll(simulators);
    }

    private async Task SimulateVehicle(int vehicleNo, CancellationToken stoppingToken)
    {
        var random = new Random();

        var journeyNo = 1;
        await SignOn(vehicleNo, journeyNo);
        long msgId = 1;

        while (!stoppingToken.IsCancellationRequested)
        {
            var randomness = random.Next(10000);

            if (randomness > 6000)
            {
                await SignOff(vehicleNo,journeyNo);
                await SignOn(vehicleNo, ++journeyNo);
            }
            else
                await SetPosition(msgId.ToString(), vehicleNo, journeyNo, randomness % 2400);

            if (randomness % 1000 < 800)
                msgId++;

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }
}