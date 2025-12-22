# Уведомления (InternetShopService_back) — интеграция фронтенда

## 1. Обзор

- **Источник истины**: REST API `/api/notifications` (лента уведомлений в БД)
- **Realtime**: SignalR `/shophub` (события “создано/обновлено/удалено + изменился счётчик”)
- **Навигация по уведомлению**: фронт использует `objectType` + `objectId`, чтобы открыть нужную сущность

Уведомления создаются автоматически на бэкенде при событиях от fimBiz:
- пришёл **новый заказ**
- изменился **статус заказа**
- пришёл **новый комментарий к заказу**

---

## 2. Авторизация

### 2.1 HTTP API
Все методы требуют:
- `Authorization: Bearer <JWT>`

### 2.2 SignalR
Подключение к hub:
- `wss://<host>/shophub?access_token=<jwt>`

После `start()` **обязательно** вызвать:
- `JoinHub()`

JWT должен содержать:
- `ClaimTypes.NameIdentifier` — `Guid` пользователя
- `CounterpartyId` — `Guid` контрагента

Сервер рассылает события **только соединениям того же `CounterpartyId`**.

---

## 3. REST API

### 3.1 Получить список уведомлений
`GET /api/notifications?page=1&pageSize=20`

Response `200 OK`:
```json
{
  "items": [
    {
      "id": "guid",
      "title": "Новый заказ",
      "description": null,
      "objectType": "Order",
      "objectId": "guid",
      "isRead": false,
      "readAt": null,
      "createdAt": "2025-12-22T12:00:00.000Z"
    }
  ],
  "totalCount": 123,
  "page": 1,
  "pageSize": 20
}
```

### 3.2 Счётчик непрочитанных
`GET /api/notifications/unread-count`

Response `200 OK`:
```json
{ "count": 3 }
```

### 3.3 Отметить уведомление прочитанным
`POST /api/notifications/{id}/read`

Response `200 OK`:
- Возвращает обновлённое уведомление (`ShopNotificationDto`)

Response `404 NotFound`:
- Если уведомление не найдено / недоступно для текущего пользователя

### 3.4 Отметить все уведомления прочитанными
`POST /api/notifications/read-all`

Response `200 OK`:
```json
{ "updated": 5 }
```

### 3.5 Удалить уведомление
`DELETE /api/notifications/{id}`

Response:
- `204 NoContent` — успех
- `404 NotFound` — не найдено/недоступно

Примечание:
- Удаление мягкое (`DeletedAt`), в выдаче удалённые уведомления не возвращаются.

---

## 4. DTO / контракты

### 4.1 ShopNotificationDto
```ts
type ShopNotificationDto = {
  id: string;               // Guid
  title: string;
  description: string | null;

  objectType: "Order" | "OrderComment";
  objectId: string;          // Guid

  isRead: boolean;
  readAt: string | null;     // ISO UTC

  createdAt: string;         // ISO UTC
};
```

### 4.2 UnreadNotificationsCountDto
```ts
type UnreadNotificationsCountDto = { count: number };
```

### 4.3 ObjectType (enum)
Серверный enum: `ShopNotificationObjectType`
- `Order = 1`
- `OrderComment = 2`

В JSON (на бэке включён `JsonStringEnumConverter`) приходит строкой:
- `"Order"` / `"OrderComment"`

---

## 5. SignalR события (server -> client)

Hub: `/shophub`

### 5.1 События уведомлений
- `NotificationCreated(ShopNotificationDto notification)`
- `NotificationUpdated(ShopNotificationDto notification)`
- `NotificationRemoved(Guid notificationId)`
- `NotificationsReadAll()`
- `UnreadNotificationsCountChanged(int count)`

Рекомендация:
- REST — источник истины
- SignalR — ускоритель UI (если событие пропущено, перечитать список/счётчик через REST)

---

## 6. Рекомендуемый сценарий на фронте

### 6.1 При старте приложения
1) Подключить SignalR и вызвать `JoinHub()`.
2) Загрузить:
- `GET /api/notifications/unread-count` → бейдж
- (опционально) `GET /api/notifications?page=1&pageSize=20` → лента

### 6.2 При клике на уведомление
1) `POST /api/notifications/{id}/read`
2) Навигация:
- если `objectType == "Order"` → открыть `/orders/{objectId}` и подгрузить заказ через `GET /api/orders/{id}`

---

## 7. Пример подключения SignalR (JS)

```js
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
  .withUrl(`${BASE_URL}/shophub?access_token=` + encodeURIComponent(token))
  .withAutomaticReconnect()
  .build();

connection.on("UnreadNotificationsCountChanged", (count) => {
  // update badge
});

connection.on("NotificationCreated", (notification) => {
  // optionally insert into list
});

await connection.start();
await connection.invoke("JoinHub");
```
