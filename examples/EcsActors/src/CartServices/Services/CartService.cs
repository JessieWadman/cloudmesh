using EcsActorsExample.Contracts;

namespace EcsActorsExample.Services
{
    public class CartService : ICartService
    {
        private readonly ILogger<CartService> logger;

        public CartService(ILogger<CartService> logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<PlacedOrder> PlaceOrderAsync(string cartId, CancellationToken cancellationToken)
        {
            logger.LogInformation("Order for cart {cartId} placed.", cartId);
            // Anybody remember Northwind sql database? :')
            // No? How about Visual FoxPro? 
            return Task.FromResult<PlacedOrder>(new(42, "Alfreds Futterkiste", "Success"));
        }
    }
}
