namespace InternetShopService_back.Shared.Services;

public interface IShopContext
{
    Guid? ShopId { get; }
    void SetShopId(Guid shopId);
}
