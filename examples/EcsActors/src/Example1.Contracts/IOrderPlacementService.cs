namespace EcsActorsExample.Contracts
{
    public interface IOrderPlacementService
    {
        Task<string> PlaceOrder(string comment);
    }
}
