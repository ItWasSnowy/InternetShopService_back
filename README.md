# InternetShopService_back

Серверное приложение для взаимодействия с кабинетами интернет магазина контрагентов.

## Архитектура

Проект разделен на два основных модуля:

1. **Модуль кабинета пользователей интернет магазина** (`Modules/UserCabinet/`)
   - Авторизация (по звонку и паролю)
   - Управление корзиной
   - Управление адресами доставки
   - Управление грузополучателями
   - Просмотр скидок и данных контрагента
   - Просмотр документов (для B2B)

2. **Модуль управления заказами (CRM)** (`Modules/OrderManagement/`)
   - Создание и управление заказами
   - Управление статусами заказов
   - Создание счетов и УПД
   - Генерация PDF документов
   - Назначение сборщиков и водителей

## Структура проекта

```
InternetShopService_back/
├── Modules/
│   ├── UserCabinet/              # Модуль кабинета пользователей
│   │   ├── Controllers/          # API контроллеры
│   │   ├── Services/            # Бизнес-логика
│   │   ├── Models/              # Модели данных модуля
│   │   ├── Repositories/        # Репозитории (будут добавлены)
│   │   └── DTOs/                # Data Transfer Objects
│   └── OrderManagement/         # Модуль управления заказами
│       ├── Controllers/
│       ├── Services/
│       ├── Models/
│       ├── Repositories/
│       └── DTOs/
├── Shared/                       # Общие компоненты
│   ├── Models/                  # Общие модели (Counterparty, Discount)
│   └── Repositories/
├── Infrastructure/              # Инфраструктурные компоненты
│   ├── Grpc/                    # gRPC клиенты для FimBiz
│   ├── Notifications/           # Сервисы уведомлений
│   └── Pdf/                    # Генерация PDF
├── Data/                        # Entity Framework
│   ├── ApplicationDbContext.cs
│   └── Configurations/         # Конфигурации EF Core
├── Middleware/                  # Промежуточное ПО
└── Configurations/              # Конфигурации приложения
```

## Технологии

- **.NET 8.0**
- **ASP.NET Core Web API**
- **PostgreSQL** (через Entity Framework Core)
- **gRPC** (для интеграции с FimBiz)
- **JWT** (для аутентификации)
- **QuestPDF** (для генерации PDF)
- **AutoMapper** (для маппинга объектов)

## Настройка

1. Установите PostgreSQL и создайте базу данных
2. Обновите строку подключения в `appsettings.json`:
   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Host=localhost;Port=5432;Database=InternetShopService;Username=postgres;Password=postgres"
   }
   ```

3. Создайте миграцию:
   ```bash
   dotnet ef migrations add InitialCreate
   dotnet ef database update
   ```

4. Запустите приложение:
   ```bash
   dotnet run
   ```

## API Endpoints

### Авторизация
- `POST /api/auth/request-code` - Запрос кода для входа по звонку
- `POST /api/auth/verify-code` - Проверка кода
- `POST /api/auth/set-password` - Установка пароля
- `POST /api/auth/login` - Вход по паролю
- `POST /api/auth/logout` - Выход

### Корзина
- `GET /api/cart` - Получить корзину
- `POST /api/cart/add` - Добавить товар в корзину
- `PUT /api/cart/{itemId}` - Обновить количество товара
- `DELETE /api/cart/{itemId}` - Удалить товар из корзины
- `DELETE /api/cart/clear` - Очистить корзину

### Контрагент
- `GET /api/counterparty/current` - Получить данные текущего контрагента
- `GET /api/counterparty/discounts` - Получить скидки контрагента

### Заказы
- `GET /api/orders` - Получить список заказов
- `GET /api/orders/{id}` - Получить детали заказа
- `POST /api/orders` - Создать заказ
- `PUT /api/orders/{id}/status` - Обновить статус заказа

## Статусы заказа

1. **Processing** - Обрабатывается
2. **AwaitingPayment** - Ожидает оплаты/Подтверждения счета
3. **InvoiceConfirmed** - Счет подтвержден
4. **Manufacturing** - Изготавливается
5. **Assembling** - Собирается
6. **TransferredToCarrier** - Передается в транспортную компанию
7. **DeliveringByCarrier** - Доставляется транспортной компанией
8. **Delivering** - Доставляется
9. **AwaitingPickup** - Ожидает получения
10. **Received** - Получен

## API Документация

Подробная документация по API для фронтенда находится в папке `docs/`:

- **[API_DOCUMENTATION.md](./docs/API_DOCUMENTATION.md)** - Полная документация с примерами
- **[API_QUICK_REFERENCE.md](./docs/API_QUICK_REFERENCE.md)** - Краткая справка по эндпоинтам
- **[ORDER_API_DOCUMENTATION.md](./docs/ORDER_API_DOCUMENTATION.md)** - Подробная документация по API заказов

## Следующие шаги

1. ✅ Реализовать авторизацию (генерация кодов, JWT токены)
2. ✅ Реализовать репозитории и сервисы
3. ✅ Настроить интеграцию с FimBiz через gRPC
4. ⏳ Реализовать логику статусов заказов
5. ⏳ Реализовать генерацию PDF для счетов и УПД
6. ✅ Добавить валидацию и обработку ошибок
7. ⏳ Добавить unit-тесты

## Примечания

- Проект находится в стадии разработки
- Некоторые сервисы содержат заглушки (NotImplementedException)
- Для работы требуется настроенная база данных PostgreSQL
- Интеграция с FimBiz через gRPC требует настройки endpoint'а

