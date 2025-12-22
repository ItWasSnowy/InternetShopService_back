using System.Threading.Tasks;
using InternetShopService_back.Modules.OrderManagement.DTOs;
using InternetShopService_back.Modules.UserCabinet.DTOs;
using UserCabinetDeliveryAddressDto = InternetShopService_back.Modules.UserCabinet.DTOs.DeliveryAddressDto;
using UserCabinetCargoReceiverDto = InternetShopService_back.Modules.UserCabinet.DTOs.CargoReceiverDto;

namespace InternetShopService_back.Infrastructure.SignalR;

public interface IShopHubClient
{
    Task ConnectionConfirmed(string message);
    Task Disconnected(string reason);

    Task OrderCreated(OrderDto order);
    Task OrderUpdated(OrderDto order);
    Task OrderDeleted(System.Guid orderId);
    Task OrderCommentAdded(OrderCommentDto comment);

    Task CounterpartyUpdated(CounterpartyDto counterparty);

    Task DeliveryAddressCreated(UserCabinetDeliveryAddressDto address);
    Task DeliveryAddressUpdated(UserCabinetDeliveryAddressDto address);
    Task DeliveryAddressDeleted(System.Guid addressId);

    Task CargoReceiverCreated(UserCabinetCargoReceiverDto receiver);
    Task CargoReceiverUpdated(UserCabinetCargoReceiverDto receiver);
    Task CargoReceiverDeleted(System.Guid receiverId);

    Task CartChanged(CartDto cart);
}
