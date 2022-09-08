using CloudMesh.Services;

namespace EcsActorsExample.Contracts
{
    public interface IFulfillmentService : IService
    {
        Task<string> CompleteOrderAsync(int orderNo, CancellationToken cancellationToken);
    }
}
