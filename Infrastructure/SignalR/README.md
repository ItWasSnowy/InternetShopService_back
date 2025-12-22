# ShopHub (SignalR)

Минимальный SignalR хаб для подключения внешнего клиента к `InternetShopService_back`.

## Endpoint

- `/shophub`

## Auth

Хаб защищён `[Authorize]` и использует JWT Bearer.

Для WebSocket/SSE подключения токен передается через query:

- `?access_token=<jwt>`

После установления соединения клиент ОБЯЗАТЕЛЬНО вызывает:

- `JoinHub()`

## Методы хаба (client -> server)

- `JoinHub()`
- `LeaveHub()`
- `Ping()`

## События (server -> client)

Определены в `IShopHubClient`:

- `ConnectionConfirmed(string message)`
- `Disconnected(string reason)`
