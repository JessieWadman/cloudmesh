using CloudMesh.Actors;
using EcsActorsExample.Contracts;

namespace EcsActorsExample.Actors
{
    public class Cart : Actor, ICart
    {
        public Cart()
        {
            SetIdleTimeout(TimeSpan.FromSeconds(3));
        }

        public Task<Order> PlaceOrderAsync(CartItem[] items)
        {
            var random = new Random();
            var randomOrderNo = random.Next(1000, 15000);
            var orderLines = items.Select(i => new OrderLine(i.SKU, random.Next(100, 15000) / 100, i.Quantity));
            var order = new Order(randomOrderNo, orderLines.ToHashSet());

            Logger.LogInformation($"Order placed from {Sender} for a total of {order.GetTotal()}");

            return Task.FromResult(order);
        }

        public Task<string> Ping()
        {
            
            return Task.FromResult("Pong");
        }

        public string NonAsync()
        {
            throw new NotImplementedException();
        }

        protected override ValueTask OnBeforeDeactivation()
        {
            Console.WriteLine($"Deactivating actor Cart#{Id}");
            return ValueTask.CompletedTask;

        }

        public async Task<string> DemoCancellation(string value, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(10000, cancellationToken);
                return "Operation was not cancelled";
            }
            catch (TaskCanceledException)
            {
                return "Operation was cancelled. Nobody will receive this.";
            }
        }
    }
}
