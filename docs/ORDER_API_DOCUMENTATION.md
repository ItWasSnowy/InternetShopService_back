# API Документация: Управление заказами

## Базовый URL
```
https://your-api-domain.com/api
```

## Аутентификация

Все эндпоинты требуют авторизации через JWT токен:
```
Authorization: Bearer {accessToken}
```

---

## 1. Создание заказа

### 1.1. Создать заказ из корзины (рекомендуемый способ)

**POST** `/api/cart/create-order`

Создает заказ из товаров в корзине пользователя. После успешного создания заказа корзина автоматически очищается. Заказ отправляется в систему FimBiz для дальнейшей обработки.

**Тело запроса:**
```json
{
  "deliveryType": 1,
  "deliveryAddressId": "123e4567-e89b-12d3-a456-426614174003",
  "cargoReceiverId": "123e4567-e89b-12d3-a456-426614174004",
  "carrierId": "123e4567-e89b-12d3-a456-426614174005"
}
```

**Параметры:**

| Параметр | Тип | Обязательный | Описание |
|----------|-----|--------------|----------|
| `deliveryType` | `integer` | ✅ Да | Тип доставки (см. таблицу ниже) |
| `deliveryAddressId` | `Guid?` | ⚠️ Условно | ID адреса доставки. Обязателен для типов доставки `Carrier` (2) и `SellerDelivery` (3) |
| `cargoReceiverId` | `Guid?` | ❌ Нет | ID грузополучателя (для юридических лиц) |
| `carrierId` | `Guid?` | ⚠️ Условно | ID транспортной компании. Обязателен для типа доставки `Carrier` (2) |

**Типы доставки (`deliveryType`):**

| Значение | Название | Описание |
|----------|----------|----------|
| `1` | `Pickup` | Самовывоз из магазина |
| `2` | `Carrier` | Доставка транспортной компанией |
| `3` | `SellerDelivery` | Доставка средствами продавца |

**Успешный ответ (201 Created):**
```json
{
  "id": "123e4567-e89b-12d3-a456-426614174006",
  "orderNumber": "ORD-2024-12-0001",
  "status": 1,
  "statusName": "Обрабатывается",
  "deliveryType": 1,
  "trackingNumber": null,
  "totalAmount": 15000.50,
  "createdAt": "2024-12-15T10:30:00Z",
  "items": [
    {
      "id": "223e4567-e89b-12d3-a456-426614174007",
      "nomenclatureId": "323e4567-e89b-12d3-a456-426614174008",
      "nomenclatureName": "Товар 1",
      "quantity": 2,
      "price": 5000.00,
      "discountPercent": 10.0,
      "totalAmount": 9000.00
    },
    {
      "id": "423e4567-e89b-12d3-a456-426614174009",
      "nomenclatureId": "523e4567-e89b-12d3-a456-426614174010",
      "nomenclatureName": "Товар 2",
      "quantity": 1,
      "price": 6000.50,
      "discountPercent": 0.0,
      "totalAmount": 6000.50
    }
  ],
  "deliveryAddress": {
    "id": "123e4567-e89b-12d3-a456-426614174003",
    "address": "ул. Ленина, д. 10, кв. 5",
    "city": "Москва",
    "region": "Московская область",
    "postalCode": "123456"
  },
  "cargoReceiver": {
    "id": "123e4567-e89b-12d3-a456-426614174004",
    "fullName": "Иванов Иван Иванович",
    "passportSeries": "1234",
    "passportNumber": "567890"
  },
  "attachments": []
}
```

**Ошибки:**

| Код | Описание |
|-----|----------|
| `400 Bad Request` | Корзина пуста, адрес/грузополучатель не найден, неверные параметры или адрес/грузополучатель не принадлежит пользователю |
| `401 Unauthorized` | Пользователь не авторизован или токен недействителен |
| `500 Internal Server Error` | Внутренняя ошибка сервера |

**Примеры запросов:**

**Самовывоз:**
```json
{
  "deliveryType": 1
}
```

**Доставка транспортной компанией:**
```json
{
  "deliveryType": 2,
  "deliveryAddressId": "123e4567-e89b-12d3-a456-426614174003",
  "carrierId": "123e4567-e89b-12d3-a456-426614174005"
}
```

**Доставка магазином:**
```json
{
  "deliveryType": 3,
  "deliveryAddressId": "123e4567-e89b-12d3-a456-426614174003"
}
```

**С грузополучателем:**
```json
{
  "deliveryType": 2,
  "deliveryAddressId": "123e4567-e89b-12d3-a456-426614174003",
  "cargoReceiverId": "123e4567-e89b-12d3-a456-426614174004",
  "carrierId": "123e4567-e89b-12d3-a456-426614174005"
}
```

---

## 2. Получение заказов

### 2.1. Получить список всех заказов пользователя

**GET** `/api/orders`

Возвращает список всех заказов текущего авторизованного пользователя, отсортированных по дате создания (новые первыми).

**Успешный ответ (200 OK):**
```json
[
  {
    "id": "123e4567-e89b-12d3-a456-426614174006",
    "orderNumber": "ORD-2024-12-0001",
    "status": 5,
    "statusName": "Собирается",
    "deliveryType": 1,
    "trackingNumber": null,
    "totalAmount": 15000.50,
    "createdAt": "2024-12-15T10:30:00Z",
    "items": [...],
    "deliveryAddress": {...},
    "cargoReceiver": {...},
    "attachments": []
  },
  {
    "id": "223e4567-e89b-12d3-a456-426614174011",
    "orderNumber": "ORD-2024-12-0002",
    "status": 10,
    "statusName": "Получен",
    "deliveryType": 2,
    "trackingNumber": "TRACK123456",
    "totalAmount": 25000.00,
    "createdAt": "2024-12-10T14:20:00Z",
    "items": [...],
    "deliveryAddress": {...},
    "cargoReceiver": null,
    "attachments": []
  }
]
```

**Ошибки:**

| Код | Описание |
|-----|----------|
| `401 Unauthorized` | Пользователь не авторизован |
| `500 Internal Server Error` | Внутренняя ошибка сервера |

---

### 2.2. Получить заказ по ID

**GET** `/api/orders/{id}`

Возвращает детальную информацию о конкретном заказе.

**Параметры URL:**

| Параметр | Тип | Описание |
|----------|-----|----------|
| `id` | `Guid` | ID заказа |

**Успешный ответ (200 OK):**
```json
{
  "id": "123e4567-e89b-12d3-a456-426614174006",
  "orderNumber": "ORD-2024-12-0001",
  "status": 5,
  "statusName": "Собирается",
  "deliveryType": 1,
  "trackingNumber": null,
  "totalAmount": 15000.50,
  "createdAt": "2024-12-15T10:30:00Z",
  "items": [
    {
      "id": "223e4567-e89b-12d3-a456-426614174007",
      "nomenclatureId": "323e4567-e89b-12d3-a456-426614174008",
      "nomenclatureName": "Товар 1",
      "quantity": 2,
      "price": 5000.00,
      "discountPercent": 10.0,
      "totalAmount": 9000.00
    }
  ],
  "deliveryAddress": {
    "id": "123e4567-e89b-12d3-a456-426614174003",
    "address": "ул. Ленина, д. 10, кв. 5",
    "city": "Москва",
    "region": "Московская область",
    "postalCode": "123456"
  },
  "cargoReceiver": null,
  "attachments": [
    {
      "id": "723e4567-e89b-12d3-a456-426614174012",
      "fileName": "invoice.pdf",
      "contentType": "application/pdf",
      "isVisibleToCustomer": true,
      "createdAt": "2024-12-15T11:00:00Z"
    }
  ]
}
```

**Ошибки:**

| Код | Описание |
|-----|----------|
| `401 Unauthorized` | Пользователь не авторизован |
| `404 Not Found` | Заказ не найден |
| `500 Internal Server Error` | Внутренняя ошибка сервера |

---

## 3. Статусы заказов

Статусы заказов представлены в виде числовых значений и текстовых названий.

| Значение | Название (statusName) | Описание |
|----------|---------------------|----------|
| `1` | `Processing` | Обрабатывается - заказ только получен, создается счет |
| `2` | `AwaitingPayment` | Ожидает оплаты/Подтверждения счета |
| `3` | `InvoiceConfirmed` | Счет подтвержден - ожидает подтверждения администратором |
| `4` | `Manufacturing` | Изготавливается - недостающие позиции в производстве |
| `5` | `Assembling` | Собирается - заказ готовится к отправке |
| `6` | `TransferredToCarrier` | Передается в транспортную компанию |
| `7` | `DeliveringByCarrier` | Доставляется транспортной компанией |
| `8` | `Delivering` | Доставляется - доставка средствами магазина |
| `9` | `AwaitingPickup` | Ожидает получения - заказ в ПВЗ или готов к самовывозу |
| `10` | `Received` | Получен - заказ передан клиенту |

**Важно:** Статусы заказов управляются системой FimBiz и автоматически синхронизируются. Клиентское приложение не может напрямую изменять статусы заказов (кроме специальных случаев, например, подтверждение счета).

---

## 4. Структура данных

### 4.1. OrderDto

| Поле | Тип | Описание |
|------|-----|----------|
| `id` | `Guid` | Уникальный идентификатор заказа |
| `orderNumber` | `string` | Номер заказа (формат: ORD-YYYY-MM-XXXX) |
| `status` | `integer` | Статус заказа (см. таблицу статусов) |
| `statusName` | `string` | Текстовое название статуса |
| `deliveryType` | `integer` | Тип доставки (1-3) |
| `trackingNumber` | `string?` | Трек-номер для отслеживания (если доступен) |
| `totalAmount` | `decimal` | Общая стоимость заказа |
| `createdAt` | `DateTime` | Дата и время создания заказа (UTC) |
| `items` | `OrderItemDto[]` | Список позиций заказа |
| `deliveryAddress` | `DeliveryAddressDto?` | Адрес доставки (если указан) |
| `cargoReceiver` | `CargoReceiverDto?` | Грузополучатель (если указан) |
| `attachments` | `OrderAttachmentDto[]` | Прикрепленные файлы (счета, УПД и т.д.) |

### 4.2. OrderItemDto

| Поле | Тип | Описание |
|------|-----|----------|
| `id` | `Guid` | Уникальный идентификатор позиции |
| `nomenclatureId` | `Guid` | ID номенклатуры товара |
| `nomenclatureName` | `string` | Название товара |
| `quantity` | `integer` | Количество |
| `price` | `decimal` | Цена за единицу |
| `discountPercent` | `decimal` | Процент скидки |
| `totalAmount` | `decimal` | Итоговая стоимость позиции (с учетом скидки) |

### 4.3. DeliveryAddressDto

| Поле | Тип | Описание |
|------|-----|----------|
| `id` | `Guid` | Уникальный идентификатор адреса |
| `address` | `string` | Адрес (улица, дом, квартира) |
| `city` | `string?` | Город |
| `region` | `string?` | Регион/область |
| `postalCode` | `string?` | Почтовый индекс |

### 4.4. CargoReceiverDto

| Поле | Тип | Описание |
|------|-----|----------|
| `id` | `Guid` | Уникальный идентификатор грузополучателя |
| `fullName` | `string` | ФИО грузополучателя |
| `passportSeries` | `string` | Серия паспорта |
| `passportNumber` | `string` | Номер паспорта |

### 4.5. OrderAttachmentDto

| Поле | Тип | Описание |
|------|-----|----------|
| `id` | `Guid` | Уникальный идентификатор вложения |
| `fileName` | `string` | Имя файла |
| `contentType` | `string` | MIME-тип файла |
| `isVisibleToCustomer` | `boolean` | Виден ли файл клиенту |
| `createdAt` | `DateTime` | Дата и время создания (UTC) |

---

## 5. Примеры использования

### 5.1. JavaScript/TypeScript (Fetch API)

```typescript
// Создание заказа из корзины
async function createOrderFromCart(deliveryType: number, deliveryAddressId?: string) {
  const response = await fetch('https://your-api-domain.com/api/cart/create-order', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${accessToken}`
    },
    body: JSON.stringify({
      deliveryType: deliveryType,
      deliveryAddressId: deliveryAddressId || null,
      cargoReceiverId: null,
      carrierId: deliveryType === 2 ? carrierId : null
    })
  });

  if (!response.ok) {
    const error = await response.json();
    throw new Error(error.error || 'Ошибка создания заказа');
  }

  return await response.json();
}

// Получение списка заказов
async function getOrders() {
  const response = await fetch('https://your-api-domain.com/api/orders', {
    headers: {
      'Authorization': `Bearer ${accessToken}`
    }
  });

  if (!response.ok) {
    throw new Error('Ошибка получения заказов');
  }

  return await response.json();
}

// Получение заказа по ID
async function getOrder(orderId: string) {
  const response = await fetch(`https://your-api-domain.com/api/orders/${orderId}`, {
    headers: {
      'Authorization': `Bearer ${accessToken}`
    }
  });

  if (!response.ok) {
    if (response.status === 404) {
      throw new Error('Заказ не найден');
    }
    throw new Error('Ошибка получения заказа');
  }

  return await response.json();
}
```

### 5.2. Axios

```typescript
import axios from 'axios';

const api = axios.create({
  baseURL: 'https://your-api-domain.com/api',
  headers: {
    'Content-Type': 'application/json'
  }
});

// Добавление токена в запросы
api.interceptors.request.use((config) => {
  const token = localStorage.getItem('accessToken');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// Создание заказа
const createOrder = async (orderData: {
  deliveryType: number;
  deliveryAddressId?: string;
  cargoReceiverId?: string;
  carrierId?: string;
}) => {
  const response = await api.post('/cart/create-order', orderData);
  return response.data;
};

// Получение заказов
const getOrders = async () => {
  const response = await api.get('/orders');
  return response.data;
};

// Получение заказа по ID
const getOrder = async (orderId: string) => {
  const response = await api.get(`/orders/${orderId}`);
  return response.data;
};
```

---

## 6. Обработка ошибок

Все ошибки возвращаются в формате:
```json
{
  "error": "Текст ошибки"
}
```

**Рекомендации по обработке:**

1. **401 Unauthorized** - Токен истек или недействителен. Необходимо обновить токен через `/api/auth/refresh` или выполнить повторную авторизацию.

2. **400 Bad Request** - Проверьте правильность переданных данных:
   - Корзина не пуста
   - Адрес доставки существует и принадлежит пользователю
   - Грузополучатель существует и принадлежит пользователю
   - Для типа доставки `Carrier` указан `carrierId`
   - Для типов доставки `Carrier` и `SellerDelivery` указан `deliveryAddressId`

3. **404 Not Found** - Заказ не найден или не принадлежит текущему пользователю.

4. **500 Internal Server Error** - Внутренняя ошибка сервера. Повторите запрос позже.

---

## 7. Важные замечания

1. **Синхронизация с FimBiz**: После создания заказ автоматически отправляется в систему FimBiz. Статусы заказов обновляются автоматически через gRPC.

2. **Очистка корзины**: После успешного создания заказа корзина автоматически очищается.

3. **Применение скидок**: Скидки применяются автоматически при создании заказа на основе настроек контрагента в FimBiz.

4. **Валидация данных**: Все ID адресов, грузополучателей и транспортных компаний проверяются на принадлежность текущему пользователю.

5. **Типы доставки**: 
   - Для `Pickup` (1) адрес доставки не требуется
   - Для `Carrier` (2) обязательны `deliveryAddressId` и `carrierId`
   - Для `SellerDelivery` (3) обязателен `deliveryAddressId`

---

## 8. Чек-лист для фронтенда

- [ ] Реализована авторизация с JWT токеном
- [ ] Токен добавляется в заголовок `Authorization` для всех запросов
- [ ] Обработка ошибок 401 (обновление токена)
- [ ] Валидация данных перед отправкой запроса на создание заказа
- [ ] Проверка наличия товаров в корзине перед созданием заказа
- [ ] Условная валидация полей в зависимости от типа доставки
- [ ] Отображение статусов заказов с понятными названиями
- [ ] Обработка пустых значений (`null`) для опциональных полей
- [ ] Отображение трек-номера, когда он доступен
- [ ] Отображение прикрепленных файлов (счета, УПД)

---

**Версия документации:** 1.0  
**Дата обновления:** 2024-12-15

