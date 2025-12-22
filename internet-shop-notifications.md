# Уведомления: как устроены в FimBiz и как подключить интернет-магазин

## 1. Термины и общий обзор

Этот документ описывает **старую v1 логику уведомлений**, которую можно повторить в интернет-магазине:

1) **DB-уведомления пользователю (`Notification`)**
- Сущность в БД FimBiz (таблица `Notifications`, EF: `DbSet<Notification>`).
- Используется как «лента уведомлений»: непрочитано/прочитано/удалено.

2) **Realtime (SignalR) доставка изменений по уведомлениям**
- Хаб v1: `NotificationHubOld`.
- Клиентский метод: `ReceiveMessage(user, message)`.
- В `message` приходит JSON строки `AppProtocolModel`.

3) **HTTP API для чтения/удаления/получения списка уведомлений**
- Контроллер v1: `NotificationController`.
- Все методы принимают/возвращают `AppProtocolModel`.

Важное:
- Этот документ **НЕ** про gRPC синхронизацию заказов и **НЕ** про `MasterHub`.

---

## 2. Сущность `Notification` (контекст)

### Где находится
- Файл: `TimeTracingServer/Databases/Databases.cs`
- Класс: `public class Notification`
- DbSet: `ApplicationContext.Notifications`

### Основные поля
- `Id` — идентификатор.
- `Title` / `Description` — текст уведомления.
- `TargetUserId` — целевой сотрудник (исторически: `Employ`/пользователь сотрудника), по факту используется как идентификатор получателя.
- `TargetProfileId` — целевой профиль сотрудника.
- `IsRead` / `DateTimeRead` — прочитано/когда.
- `IsImportant` — важное уведомление.
- `ShowOnAllProfiles` — показывать на всех профилях пользователя.
- `TypeObject` — тип целевого объекта (`Task`, `Request`, `Href`, `CompanyInvite`, …).
- `TargetIdObject` — id целевого объекта.
- `TargetObject` — строковый «объект» (например URL или id админ-панели).

### Где создается
На уровне сервера FimBiz уведомления обычно создаются через:
- `TimeTracingServer/v1/Services/NotificationRegister.cs` → `NotificationRegister.Add(...)`
- Примеры use-case: `TimeTracingServer/v2/DDD/Employees/UseCases/EmployeeNotificationUseCases.cs`

### Как доставляется пользователю
- Запись в БД + (опционально) push в мобильное приложение через FCM внутри `NotificationRegister.Add`.
- Веб/клиент может получать список уведомлений через v1 контроллер:
  - `TimeTracingServer/v1/Controllers/NotificationController.cs` (методы `GetList`, `Reading`, `Remove`, `ReadingAll`).

Важно: если интернет-магазин повторяет v1 механику, то основной интерфейс — это `NotificationController` + SignalR `/notificationhub`.

---

## 3. Интеграция интернет-магазина: каналы уведомлений

Интернет-магазин, если повторяет v1 логику, должен уметь:

1) Получать список уведомлений для текущего пользователя/профиля.
2) Отмечать уведомление прочитанным.
3) Удалять уведомление.
4) Отмечать все уведомления прочитанными.
5) Подключаться к SignalR и получать realtime-события (появилось новое уведомление / уведомление прочитано / удалено).

---

## 4. v1 протокол интеграции для интернет-магазина

### 4.1. HTTP API (v1 NotificationController)

Контроллер:
- `TimeTracingServer/v1/Controllers/NotificationController.cs`

Route-шаблон:
```
/Notification/<Action>
```

Методы:

1) `POST /Notification/GetList`
- Вход: `AppProtocolModel`
- `data.Body` используется как `offset` (страница), сервер делает `Skip(offset * 20).Take(20)`.
- Ответ: `AppProtocolModel.Body` содержит JSON `NotificationList`.

2) `POST /Notification/Reading`
- Вход: `AppProtocolModel`, где `data.Body` — JSON объекта `Notification` (минимум нужен `Id`).
- Ответ: `AppProtocolModel.Body` — JSON обновленного `Notification`.

3) `POST /Notification/Remove`
- Вход: `AppProtocolModel`, где `data.Body` — JSON объекта `Notification` (минимум нужен `Id`).
- Ответ: `AppProtocolModel.Body` — JSON удаленного `Notification`.

4) `POST /Notification/ReadingAll`
- Вход: `AppProtocolModel`.
- Ответ: `Successfully=true`.

Авторизация:
- Методы помечены `[Authorize]`.
- Ожидается авторизация стандартным способом сервера (cookie/JWT, зависит от вашего клиента).

Мобильный режим:
- Если `data.ChangParam == "mobile"`, контроллер ищет пользователя по `data.Email`.

### 4.2. Формат сообщений: `AppProtocolModel`

Модель:
- `TimeTracingServer/v1/Models/AppProtocolModel.cs`

Ключевые поля:
- `Body` — payload (часто JSON внутри строки).
- `ChangParam` — «тип сообщения»/маркер.
- `TargetCompanyId` — компания.
- `OrganizationId` — организация.
- `ReceiverProfileId` — профиль-получатель.
- `TimeOffset` — смещение времени (используется в GetList для `DateTimeCreate`).

### 4.3. Формат ответа GetList: `NotificationList`

Возвращается внутри `AppProtocolModel.Body`:
- `Notifications: List<Notification>`
- `CountNotification: int` (кол-во непрочитанных в текущей выдаче)

### 4.4. SignalR (v1 NotificationHubOld)

Хаб:
- `TimeTracingServer/v1/Services/NotificationHub.cs` → `NotificationHubOld`

Endpoint (из `Program.cs`):
```
ws(s)://<host>/notificationhub
```

Клиентский метод:
- `ReceiveMessage(string user, string message)`

Формат `message`:
- JSON сериализованный `AppProtocolModel`.

### 4.5. ChangParam события, которые приходят через SignalR

На основании кода v1 встречаются (минимальный обязательный набор для уведомлений):

- `"new notification for employ"`
  - `Body` содержит JSON уведомления (`Notification`).

- `"notify readed"`
  - `Body` содержит JSON уведомления (`Notification`) после установки `IsRead=true`.

- `"notify removed"`
  - `Body` содержит JSON удаленного уведомления (`Notification`).

- `"notify readed all"`
  - `Body` может быть пустым (важны `TargetCompanyId/OrganizationId/ReceiverProfileId`).

---

## 5. Примеры запросов/сообщений (v1)

### 5.1. Получить список уведомлений

`POST /Notification/GetList`

Пример `AppProtocolModel`:

```json
{
  "Body": "0",
  "ChangParam": "",
  "TargetCompanyId": 123,
  "TimeOffset": 5
}
```

Пример ответа (схематично):

```json
{
  "Successfully": true,
  "Body": "{\"Notifications\":[{...}],\"CountNotification\":3}",
  "ErrorCode": 200,
  "ErrorMessage": ""
}
```

### 5.2. Прочитать уведомление

`POST /Notification/Reading`

```json
{
  "Body": "{\"Id\": 100500}",
  "TargetCompanyId": 123,
  "TimeOffset": 5
}
```

### 5.3. Удалить уведомление

`POST /Notification/Remove`

```json
{
  "Body": "{\"Id\": 100500}",
  "TargetCompanyId": 123
}
```

### 5.4. Отметить все прочитанными

`POST /Notification/ReadingAll`

```json
{
  "Body": "",
  "TargetCompanyId": 123
}
```

### 5.5. SignalR: новое уведомление

Через `/notificationhub` будет вызвано `ReceiveMessage("", "<json>")`, где `<json>` — сериализованный `AppProtocolModel`:

```json
{
  "ChangParam": "new notification for employ",
  "Body": "{...Notification...}",
  "TargetCompanyId": 123,
  "OrganizationId": 77,
  "ReceiverProfileId": 555
}
```

---

## 6. Рекомендации по реализации на стороне интернет-магазина

1) Делайте локальное состояние «ленты уведомлений» источником истины.
2) SignalR используйте как ускоритель:
   - при `new notification for employ` можно подтягивать список с сервера или вставлять уведомление из `Body`.
3) Везде учитывайте `ReceiverProfileId` и `TargetCompanyId`.
4) Пейджинг в `GetList` — это страницы по 20 элементов:
   - `Body="0"` → первая страница,
   - `Body="1"` → вторая и т.д.

---

## 7. Чек-лист внедрения v1 уведомлений в интернет-магазин

1) Реализовать хранение `Notification` в БД магазина (или использовать FimBiz как источник и кэшировать у себя).
2) Реализовать вызовы HTTP методов:
   - `/Notification/GetList`
   - `/Notification/Reading`
   - `/Notification/Remove`
   - `/Notification/ReadingAll`
3) Реализовать SignalR подключение к `/notificationhub` и обработчик `ReceiveMessage`.
4) Реализовать обработку `ChangParam`:
   - `new notification for employ`
   - `notify readed`
   - `notify removed`
   - `notify readed all`
5) Проверить UX:
   - бейдж непрочитанных,
   - переход по `TypeObject/TargetIdObject`.

---

## 10. Связанные файлы в репозитории (быстрые ссылки)

- Документация:
  - `TimeTracingServer/docs/internet-shop-notifications.md` (этот файл)

- DB уведомления (entity):
  - `TimeTracingServer/Databases/Databases.cs` → `Notification`
  - `TimeTracingServer/v1/Services/NotificationRegister.cs`
  - `TimeTracingServer/v1/Controllers/NotificationController.cs`

- SignalR v1:
  - `TimeTracingServer/v1/Services/NotificationHub.cs` → `NotificationHubOld`
  - `TimeTracingServer/Program.cs` → `app.MapHub<NotificationHubOld>("/notificationhub")`
