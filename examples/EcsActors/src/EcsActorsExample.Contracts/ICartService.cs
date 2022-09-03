using CloudMesh.Services;

namespace EcsActorsExample.Contracts
{
    public record PlacedOrder(int OrderNo, string CustomerName, string Comment);

    public interface ICartService : IService
    {
        Task<PlacedOrder> PlaceOrderAsync(string customerName, string comment, CancellationToken cancellationToken);

        Task<string> TestCT1(CancellationToken cancellationToken);
        Task<string> TestCT2(int i, CancellationToken cancellationToken);
        Task<string> TestCT3(int i, string b, CancellationToken cancellationToken);
        Task<string> TestCT4(CancellationToken cancellationToken, int i);
        Task<string> TestCT5(CancellationToken cancellationToken, int i, int j);
        Task<string> TestCT6(int i, CancellationToken cancellationToken, int j);
        Task<string> TestCT7(int i, int j, CancellationToken cancellationToken, int k);
        Task<string> TestCT8(int i, int j, CancellationToken cancellationToken, int k, int l);

        Task<string> TryException();
    }
}
