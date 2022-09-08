using EcsActorsExample.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace OrderService.Services
{
    public class Orders : IOrderPlacementService, IFulfillmentService
    {
        public Task<string> CompleteOrderAsync(int orderNo, CancellationToken cancellationToken)
        {
            return Task.FromResult("Order completed");
        }

        public Task<string> PlaceOrder(string comment)
        {
            return Task.FromResult(comment.ToUpper());
        }
    }
}
