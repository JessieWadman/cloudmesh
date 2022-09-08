using CloudMesh.Actors;
using CloudMesh.Routing;
using CloudMesh.Services;
using EcsActorsExample.Contracts;
using System.Diagnostics;

namespace EcsActorsExample.Services
{
    public class RandomInvokeService : BackgroundService
    {
        private readonly ILogger<RandomInvokeService> logger;
        private static readonly ActivitySource Activity = new(nameof(RandomInvokeService));

        public RandomInvokeService(ILogger<RandomInvokeService> logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var random = new Random();
            var counter = 0;

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(stoppingToken).ReturnOnCancellation(false))
            {
                using var activity = Activity.StartActivity(nameof(ExecuteAsync));

                /*
                // Call lambda service
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    logger.LogInformation("Invoking order service (lives in lambda)...");
                    var orderService = ServiceProxy.Create<IFulfillmentService>();
                    var reply = await orderService.CompleteOrderAsync(42, timeout.Token);
                    logger.LogInformation($"Reply: {reply}");
                }
                catch (OperationCanceledException)
                {
                    logger.LogError($"Unexpected timeout calling service in lambda after 10 seconds.");
                }
                catch (Exception error)
                {
                    // Includes StackTrace from remote system :-)
                    logger.LogInformation(error.ToString());
                }*/

                // Call actor
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var actorNo = (counter++ % 10).ToString();
                    logger.LogInformation($"Calling cart actor #{actorNo} (lives in ecs task)");

                    var proxy = ActorProxy.Create<ICart>(actorNo.ToString());
                    var reply = await proxy.AddProductToCart("shampoo", timeout.Token);
                    Console.WriteLine($"Reply: {reply}");
                }
                catch (RoutingException re)
                {
                    logger.LogError($"Routing error: {re.Message}");
                }
                catch (OperationCanceledException)
                {
                    logger.LogError($"Unexpected timeout calling actor after 10 seconds.");
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, ex.ToString());
                }

                // Call service in ECS task
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    logger.LogInformation("Invoking cart service (lives in ecs task)...");
                    var cartService = ServiceProxy.Create<ICartService>();
                    var reply = await cartService.PlaceOrderAsync("12345", timeout.Token);
                    logger.LogInformation($"Reply: {reply}");
                }
                catch (OperationCanceledException)
                {
                    logger.LogError($"Unexpected timeout calling service after 10 seconds.");
                }
                catch (Exception error)
                {
                    // Includes StackTrace from remote system :-)
                    logger.LogInformation(error.ToString());
                }                
            }
        }
    }
}
