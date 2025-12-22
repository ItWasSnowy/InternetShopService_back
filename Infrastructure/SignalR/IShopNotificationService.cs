using System;
using System.Threading.Tasks;
using InternetShopService_back.Modules.OrderManagement.DTOs;
using InternetShopService_back.Modules.UserCabinet.DTOs;
using UserCabinetDeliveryAddressDto = InternetShopService_back.Modules.UserCabinet.DTOs.DeliveryAddressDto;
using UserCabinetCargoReceiverDto = InternetShopService_back.Modules.UserCabinet.DTOs.CargoReceiverDto;

namespace InternetShopService_back.Infrastructure.SignalR;

public interface IShopNotificationService
{
    Task OrderCreated(Guid counterpartyId, OrderDto order);
    Task OrderUpdated(Guid counterpartyId, OrderDto order);
    Task OrderDeleted(Guid counterpartyId, Guid orderId);

    Task OrderCommentAdded(Guid counterpartyId, OrderCommentDto comment);

    Task CounterpartyUpdated(Guid counterpartyId, CounterpartyDto counterparty);

    Task DeliveryAddressCreated(Guid counterpartyId, UserCabinetDeliveryAddressDto address);
    Task DeliveryAddressUpdated(Guid counterpartyId, UserCabinetDeliveryAddressDto address);
    Task DeliveryAddressDeleted(Guid counterpartyId, Guid addressId);

    Task CargoReceiverCreated(Guid counterpartyId, UserCabinetCargoReceiverDto receiver);
    Task CargoReceiverUpdated(Guid counterpartyId, UserCabinetCargoReceiverDto receiver);
    Task CargoReceiverDeleted(Guid counterpartyId, Guid receiverId);

    Task CartChanged(Guid counterpartyId, CartDto cart);
}
