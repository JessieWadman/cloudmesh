using CloudMesh.Actors;

namespace EcsActorsExample.Contracts
{
    public record OrderLine(string SKU, decimal UnitPrice, int Quantity);

    public record Order(long OrderNo, HashSet<OrderLine> Lines);

    public record CartItem(string SKU, int Quantity);

    public static class OrderExtensions
    {
        public static decimal GetTotal(this Order order)
        {
            if (order.Lines.Count == 0)
                return 0;
            return order.Lines.Sum(o => o.Quantity * o.UnitPrice);
        }
    }


    public interface ICart : IActor
    {
        Task<string> Ping();
        Task<Order> PlaceOrderAsync(CartItem[] items);
        string NonAsync();

        Task<string> DemoCancellation(string value, CancellationToken cancellationToken);
    }
}