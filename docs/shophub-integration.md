# ShopHub — интеграция SignalR (InternetShopService_back)

`ShopHub` — SignalR-хаб для реального времени для клиента интернет-магазина.

## URL подключения

- Прод: `wss://<host>/shophub`
- Локально: `ws://localhost:<port>/shophub`

## Аутентификация

Хаб защищён `[Authorize]` и использует JWT Bearer.

Для SignalR токен передаётся через query string:

- `?access_token=<jwt>`

JWT должен содержать:

- `ClaimTypes.NameIdentifier` — `Guid` пользователя
- `CounterpartyId` — `Guid` контрагента (используется для фильтрации получателей)

Важно: сервер рассылает события **только соединениям того же `CounterpartyId`**, что и у события.

## Жизненный цикл подключения

1) Создать соединение и `start()`
2) Обязательно вызвать `JoinHub()`
3) Рекомендуется слать `Ping()` раз в 20–30 секунд (или настроить на клиенте reconnection)

### Пример (JavaScript)

```js
import * as signalR from "@microsoft/signalr";

const token = "<JWT>";

const connection = new signalR.HubConnectionBuilder()
  .withUrl("https://<host>/shophub?access_token=" + encodeURIComponent(token))
  .withAutomaticReconnect()
  .build();

connection.on("ConnectionConfirmed", (msg) => console.log("confirmed:", msg));
connection.on("Disconnected", (reason) => console.error("disconnected:", reason));

// Orders
connection.on("OrderCreated", (order) => console.log("OrderCreated", order));
connection.on("OrderUpdated", (order) => console.log("OrderUpdated", order));
connection.on("OrderDeleted", (orderId) => console.log("OrderDeleted", orderId));

// Order comments
connection.on("OrderCommentAdded", (comment) => console.log("OrderCommentAdded", comment));

// Counterparty
connection.on("CounterpartyUpdated", (counterparty) => console.log("CounterpartyUpdated", counterparty));

// Delivery addresses
connection.on("DeliveryAddressCreated", (address) => console.log("DeliveryAddressCreated", address));
connection.on("DeliveryAddressUpdated", (address) => console.log("DeliveryAddressUpdated", address));
connection.on("DeliveryAddressDeleted", (addressId) => console.log("DeliveryAddressDeleted", addressId));

// Cargo receivers
connection.on("CargoReceiverCreated", (receiver) => console.log("CargoReceiverCreated", receiver));
connection.on("CargoReceiverUpdated", (receiver) => console.log("CargoReceiverUpdated", receiver));
connection.on("CargoReceiverDeleted", (receiverId) => console.log("CargoReceiverDeleted", receiverId));

// Cart
connection.on("CartChanged", (cart) => console.log("CartChanged", cart));

await connection.start();
await connection.invoke("JoinHub");

setInterval(() => {
  if (connection.state === signalR.HubConnectionState.Connected) {
    connection.invoke("Ping").catch(() => {});
  }
}, 30000);
```

## Методы хаба (client -> server)

- `JoinHub()` — обязательная регистрация соединения (привязка к `CounterpartyId` из JWT)
- `LeaveHub()` — ручное отключение (опционально)
- `Ping()` — heartbeat (обновляет активность)

## События (server -> client)

### Системные

- `ConnectionConfirmed(string message)`
- `Disconnected(string reason)`

### Заказы

- `OrderCreated(OrderDto order)`
- `OrderUpdated(OrderDto order)` — включая изменение статуса (отдельного события по статусу нет)
- `OrderDeleted(Guid orderId)`

`OrderDto` берётся из `Modules/OrderManagement/DTOs/OrderDto.cs`.

### Комментарии к заказам

- `OrderCommentAdded(OrderCommentDto comment)`

`OrderCommentDto` берётся из `Modules/OrderManagement/DTOs/OrderCommentDto.cs`.

Комментарий может прийти:

- из API (когда клиент интернет-магазина создает комментарий)
- из gRPC (когда FimBiz отправляет комментарий)

### Контрагент

- `CounterpartyUpdated(CounterpartyDto counterparty)`

`CounterpartyDto` берётся из `Modules/UserCabinet/DTOs/CounterpartyDto.cs`.

### Адреса доставки

- `DeliveryAddressCreated(DeliveryAddressDto address)`
- `DeliveryAddressUpdated(DeliveryAddressDto address)`
- `DeliveryAddressDeleted(Guid addressId)`

`DeliveryAddressDto` берётся из `Modules/UserCabinet/DTOs/DeliveryAddressDto.cs`.

### Грузополучатели

- `CargoReceiverCreated(CargoReceiverDto receiver)`
- `CargoReceiverUpdated(CargoReceiverDto receiver)`
- `CargoReceiverDeleted(Guid receiverId)`

`CargoReceiverDto` берётся из `Modules/UserCabinet/DTOs/CargoReceiverDto.cs`.

### Корзина

- `CartChanged(CartDto cart)`

`CartDto` берётся из `Modules/UserCabinet/DTOs/CartDto.cs`.

## Таймауты сервера

Для `/shophub` настроены (переопределение HubOptions только для этого хаба):

- `KeepAliveInterval = 8s`
- `ClientTimeoutInterval = 20s`
- `HandshakeTimeout = 30s`
