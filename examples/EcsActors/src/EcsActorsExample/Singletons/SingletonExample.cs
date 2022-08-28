using CloudMesh.Actors.Singletons;

namespace EcsActorsExample.Singletons
{
    public class SingletonExample : Singleton
    {
        private readonly ILogger<SingletonExample> logger;
        private long counter;

        public SingletonExample(ILogger<SingletonExample> logger)
            : base()
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation($"{SingletonName} running on instance {InstanceId}");
                await SetUserDataAsync($"{SingletonName}#{++counter}");
                try
                {
                    await Task.Delay(5000, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
            logger.LogInformation("Exiting singleton!");
        }
    }
}
