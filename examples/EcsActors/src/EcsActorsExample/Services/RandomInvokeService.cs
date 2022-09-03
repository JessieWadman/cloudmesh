using CloudMesh.Actors;
using CloudMesh.Routing;
using CloudMesh.Services;
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

                        using var cts3 = new CancellationTokenSource(1500);
                        try
                        {
                            await proxy.DemoCancellation("1", cts3.Token);
                        }
                        catch (TaskCanceledException)
                        {
                            // Operation was cancelled, as expected.
                        }
                        catch (Exception error)
                        {
                            Console.WriteLine(error.ToString());
                            throw;
                        }
                    }
                    catch (RoutingException re)
                    {
                        logger.LogError(re.Message);
                    }
                    catch (Exception ex)
                    {
                        logger.LogCritical(ex, ex.ToString());
                    }
                    
                    var cartService = ServiceProxy.Create<ICartService>();

                    /*
                    try
                    {
                        await cartService.TryException();
                    }
                    catch (Exception error)
                    {
                        // Includes StackTrace from remote system :-)
                        logger.LogInformation(error.ToString());
                    }*/

                    using var cts = new CancellationTokenSource(2500);
                    try
                    {
                        var retVal = await cartService.PlaceOrderAsync("ALFK", "Some comment", cts.Token);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }

                    using var cts2 = new CancellationTokenSource();
                    _ = await cartService.TestCT1(cts2.Token); // Only one
                    _ = await cartService.TestCT2(1, cts2.Token); // at end 
                    _ = await cartService.TestCT3(1, "2", cts2.Token);
                    _ = await cartService.TestCT4(cts2.Token, 1); // At start
                    _ = await cartService.TestCT5(cts2.Token, 1, 2);
                    _ = await cartService.TestCT6(1, cts2.Token, 2); // In the middle
                    _ = await cartService.TestCT7(1, 2, cts2.Token, 3);
                }
            }
        }
    }
}
