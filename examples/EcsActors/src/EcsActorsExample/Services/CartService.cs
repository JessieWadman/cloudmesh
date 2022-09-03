using EcsActorsExample.Contracts;

namespace EcsActorsExample.Services
{
    public class CartService : ICartService
    {
        public Task<string> TryException()
        {
            throw new InvalidOperationException("Testing exceptions!");
        }

        public async Task<PlacedOrder> PlaceOrderAsync(string customerName, string comment, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(10000, cancellationToken);
            }
            catch (Exception x)
            {
                Console.WriteLine($"PlaceOrderAsync cancelled: {x}");
            }
            return new(1, customerName, comment);
        }

        public Task<string> TestCT1(CancellationToken cancellationToken) => Task.FromResult("OK");
        public Task<string> TestCT2(int i, CancellationToken cancellationToken) => Task.FromResult("OK");
        public Task<string> TestCT3(int i, string b, CancellationToken cancellationToken) => Task.FromResult("OK");
        public Task<string> TestCT4(CancellationToken cancellationToken, int i) => Task.FromResult("OK");
        public Task<string> TestCT5(CancellationToken cancellationToken, int i, int j) => Task.FromResult("OK");
        public Task<string> TestCT6(int i, CancellationToken cancellationToken, int j) => Task.FromResult("OK");
        public Task<string> TestCT7(int i, int j, CancellationToken cancellationToken, int k) => Task.FromResult("OK");
        public Task<string> TestCT8(int i, int j, CancellationToken cancellationToken, int k, int l) => Task.FromResult("OK");
    }
}
