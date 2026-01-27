namespace InternetShopService_back.Shared.Services;

public class ShopContext : IShopContext
{
    public Guid? ShopId { get; private set; }

    public void SetShopId(Guid shopId)
    {
        ShopId = shopId;
    }
}
