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

        public Task<string> AddProductToCart(string productId, CancellationToken cancellationToken)
        {
            Logger.LogInformation($"Product [{productId}] added to cart {Id}");
            return Task.FromResult("OK");
        }
    }
}
