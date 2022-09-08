using CloudMesh.Services;

namespace EcsActorsExample.Contracts
{
    public record PlacedOrder(int OrderNo, string CustomerName, string Comment);

    public interface ICartService : IService
    {
        Task<PlacedOrder> PlaceOrderAsync(string cardId, CancellationToken cancellationToken);        
    }
}
