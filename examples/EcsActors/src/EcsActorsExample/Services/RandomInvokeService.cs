using CloudMesh.Actors.Client;
using CloudMesh.Actors.Routing;
using EcsActorsExample.Contracts;

namespace EcsActorsExample.Services
{
    public class RandomInvokeService : BackgroundService
    {
        private readonly ILogger<RandomInvokeService> logger;

        public RandomInvokeService(ILogger<RandomInvokeService> logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var random = new Random();
            var counter = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                if (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var actorNo = (counter++ % 10).ToString();

                        var proxy = ActorProxy.Create<ICart>(actorNo.ToString());
                        var reply = await proxy.Ping();
                        Console.WriteLine(reply);
                    }
                    catch (RoutingException re)
                    {
                        logger.LogError(re.Message);
                    }
                    catch (Exception ex)
                    {
                        logger.LogCritical(ex, ex.ToString());
                    }

                }
            }
        }
    }
}
