namespace InternetShopService_back.Modules.OrderManagement.Models;

public enum OrderStatus
{
    Processing = 1,                    // Обрабатывается
    AwaitingPayment = 2,               // Ожидает оплаты/Подтверждения счета
    InvoiceConfirmed = 3,              // Счет подтвержден
    Manufacturing = 4,                 // Изготавливается
    Assembling = 5,                    // Собирается
    TransferredToCarrier = 6,          // Передается в транспортную компанию
    DeliveringByCarrier = 7,           // Доставляется транспортной компанией
    Delivering = 8,                    // Доставляется
    AwaitingPickup = 9,                // Ожидает получения
    Received = 10                      // Получен
}

