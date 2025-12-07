# API Документация: Работа с корзиной

Данная документация описывает API для работы с корзиной покупок в интернет-магазине.

## Базовый URL

```
https://test.fimbiz.ru/api/cart
```

Для локальной разработки:
```
http://localhost:5133/api/cart
```

## Аутентификация

Все запросы к API корзины требуют JWT токен в заголовке `Authorization`:

```
Authorization: Bearer <your_access_token>
```

Токен получается после авторизации через `/api/auth/verify-code` или `/api/auth/login`.

---

## Endpoints

### 1. Получить корзину пользователя

Получает текущую корзину пользователя со всеми товарами и примененными скидками.

**Endpoint:** `GET /api/cart`

**Заголовки:**
```
Authorization: Bearer <access_token>
Content-Type: application/json
```

**Ответ (200 OK):**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "items": [
    {
      "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "nomenclatureId": "550e8400-e29b-41d4-a716-446655440000",
      "nomenclatureName": "Товар 1",
      "quantity": 2,
      "price": 1000.00,
      "unitType": "шт",
      "sku": "SKU-001",
      "urlPhotos": [
        "https://example.com/photo1.jpg",
        "https://example.com/photo2.jpg"
      ],
      "discountPercent": 10.0,
      "priceWithDiscount": 900.00,
      "totalAmount": 1800.00
    }
  ],
  "totalAmount": 1800.00
}
```

**Ошибки:**
- `401 Unauthorized` - пользователь не авторизован
- `500 Internal Server Error` - внутренняя ошибка сервера

**Пример запроса (JavaScript/TypeScript):**
```typescript
const response = await fetch('https://test.fimbiz.ru/api/cart', {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${accessToken}`,
    'Content-Type': 'application/json'
  }
});

const cart = await response.json();
```

---

### 2. Добавить один товар в корзину

Добавляет один товар в корзину. Если товар уже есть в корзине, увеличивает его количество.

**Endpoint:** `POST /api/cart/add`

**Заголовки:**
```
Authorization: Bearer <access_token>
Content-Type: application/json
```

**Тело запроса:**
```json
{
  "nomenclatureId": "550e8400-e29b-41d4-a716-446655440000",
  "nomenclatureName": "Название товара",
  "quantity": 1,
  "price": 1000.00,
  "unitType": "шт",
  "sku": "SKU-001",
  "urlPhotos": [
    "https://example.com/photo1.jpg",
    "https://example.com/photo2.jpg"
  ]
}
```

**Поля:**
| Поле | Тип | Обязательное | Описание |
|------|-----|--------------|----------|
| `nomenclatureId` | GUID (string) | Да | Уникальный идентификатор номенклатуры товара |
| `nomenclatureName` | string | Да | Название товара (макс. 500 символов) |
| `quantity` | integer | Да | Количество товара (минимум 1) |
| `price` | decimal | Да | Цена товара (не может быть отрицательной) |
| `unitType` | string | Нет | Единица измерения (макс. 50 символов), например: "шт", "кг", "л" |
| `sku` | string | Нет | Артикул товара (макс. 100 символов) |
| `urlPhotos` | array of string | Нет | Массив URL фотографий товара |

**Ответ (200 OK):**
Возвращает обновленную корзину (такой же формат, как в `GET /api/cart`).

**Особенности:**
- Если товар с таким `nomenclatureId` уже есть в корзине, его количество увеличится на указанное значение
- Цена товара обновится на актуальную (из запроса)
- Опциональные поля (`unitType`, `sku`, `urlPhotos`) обновятся только если они указаны

**Ошибки:**
- `400 Bad Request` - неверные данные (например, некорректные поля)
- `401 Unauthorized` - пользователь не авторизован
- `500 Internal Server Error` - внутренняя ошибка сервера

**Пример запроса:**
```typescript
const addItemRequest = {
  nomenclatureId: "550e8400-e29b-41d4-a716-446655440000",
  nomenclatureName: "Ноутбук Dell XPS 13",
  quantity: 1,
  price: 89990.00,
  unitType: "шт",
  sku: "DELL-XPS13-2024",
  urlPhotos: [
    "https://example.com/laptop-front.jpg",
    "https://example.com/laptop-side.jpg"
  ]
};

const response = await fetch('https://test.fimbiz.ru/api/cart/add', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${accessToken}`,
    'Content-Type': 'application/json'
  },
  body: JSON.stringify(addItemRequest)
});

const updatedCart = await response.json();
```

---

### 3. Добавить несколько товаров в корзину

Добавляет список товаров в корзину за один запрос. Удобно для массового добавления.

**Endpoint:** `POST /api/cart/add-items`

**Заголовки:**
```
Authorization: Bearer <access_token>
Content-Type: application/json
```

**Тело запроса:**
```json
[
  {
    "nomenclatureId": "550e8400-e29b-41d4-a716-446655440000",
    "nomenclatureName": "Товар 1",
    "quantity": 2,
    "price": 1000.00,
    "unitType": "шт",
    "sku": "SKU-001"
  },
  {
    "nomenclatureId": "660e8400-e29b-41d4-a716-446655440001",
    "nomenclatureName": "Товар 2",
    "quantity": 1,
    "price": 500.00,
    "unitType": "шт",
    "sku": "SKU-002"
  }
]
```

**Ответ (200 OK):**
Возвращает обновленную корзину со всеми добавленными товарами.

**Ошибки:**
- `400 Bad Request` - список пустой или неверные данные
- `401 Unauthorized` - пользователь не авторизован
- `500 Internal Server Error` - внутренняя ошибка сервера

**Пример запроса:**
```typescript
const items = [
  {
    nomenclatureId: "550e8400-e29b-41d4-a716-446655440000",
    nomenclatureName: "Товар 1",
    quantity: 2,
    price: 1000.00,
    unitType: "шт"
  },
  {
    nomenclatureId: "660e8400-e29b-41d4-a716-446655440001",
    nomenclatureName: "Товар 2",
    quantity: 1,
    price: 500.00,
    unitType: "шт"
  }
];

const response = await fetch('https://test.fimbiz.ru/api/cart/add-items', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${accessToken}`,
    'Content-Type': 'application/json'
  },
  body: JSON.stringify(items)
});

const updatedCart = await response.json();
```

---

### 4. Обновить количество товара в корзине

Изменяет количество конкретного товара в корзине.

**Endpoint:** `PUT /api/cart/{itemId}`

**Параметры пути:**
- `itemId` (GUID) - идентификатор позиции в корзине (не `nomenclatureId`!)

**Заголовки:**
```
Authorization: Bearer <access_token>
Content-Type: application/json
```

**Тело запроса:**
```json
{
  "quantity": 5
}
```

**Поля:**
| Поле | Тип | Обязательное | Описание |
|------|-----|--------------|----------|
| `quantity` | integer | Да | Новое количество товара (минимум 1) |

**Ответ (200 OK):**
Возвращает обновленную корзину.

**Ошибки:**
- `400 Bad Request` - неверное количество (меньше 1)
- `401 Unauthorized` - пользователь не авторизован или товар не принадлежит пользователю
- `404 Not Found` - товар не найден в корзине
- `500 Internal Server Error` - внутренняя ошибка сервера

**Пример запроса:**
```typescript
const itemId = "7c9e6679-7425-40de-944b-e07fc1f90ae7"; // ID позиции в корзине

const response = await fetch(`https://test.fimbiz.ru/api/cart/${itemId}`, {
  method: 'PUT',
  headers: {
    'Authorization': `Bearer ${accessToken}`,
    'Content-Type': 'application/json'
  },
  body: JSON.stringify({ quantity: 5 })
});

const updatedCart = await response.json();
```

---

### 5. Удалить товар из корзины

Удаляет одну позицию товара из корзины.

**Endpoint:** `DELETE /api/cart/{itemId}`

**Параметры пути:**
- `itemId` (GUID) - идентификатор позиции в корзине

**Заголовки:**
```
Authorization: Bearer <access_token>
```

**Ответ (204 No Content):**
Успешное удаление, тело ответа пустое.

**Ошибки:**
- `401 Unauthorized` - пользователь не авторизован или товар не принадлежит пользователю
- `404 Not Found` - товар не найден в корзине
- `500 Internal Server Error` - внутренняя ошибка сервера

**Пример запроса:**
```typescript
const itemId = "7c9e6679-7425-40de-944b-e07fc1f90ae7";

const response = await fetch(`https://test.fimbiz.ru/api/cart/${itemId}`, {
  method: 'DELETE',
  headers: {
    'Authorization': `Bearer ${accessToken}`
  }
});

if (response.status === 204) {
  console.log('Товар успешно удален из корзины');
}
```

---

### 6. Очистить корзину

Удаляет все товары из корзины.

**Endpoint:** `DELETE /api/cart/clear`

**Заголовки:**
```
Authorization: Bearer <access_token>
```

**Ответ (204 No Content):**
Успешная очистка, тело ответа пустое.

**Ошибки:**
- `401 Unauthorized` - пользователь не авторизован
- `404 Not Found` - корзина не найдена
- `500 Internal Server Error` - внутренняя ошибка сервера

**Пример запроса:**
```typescript
const response = await fetch('https://test.fimbiz.ru/api/cart/clear', {
  method: 'DELETE',
  headers: {
    'Authorization': `Bearer ${accessToken}`
  }
});

if (response.status === 204) {
  console.log('Корзина успешно очищена');
}
```

---

### 7. Создать заказ из корзины

Создает заказ на основе товаров из корзины. После создания заказа корзина очищается.

**Endpoint:** `POST /api/cart/create-order`

**Заголовки:**
```
Authorization: Bearer <access_token>
Content-Type: application/json
```

**Тело запроса:**
```json
{
  "deliveryType": 1,
  "deliveryAddressId": "8d9e6679-7425-40de-944b-e07fc1f90ae8",
  "cargoReceiverId": "9e0f7780-8536-51ef-a55c-f18fd2g01bf9",
  "carrierId": "0f1g8891-9647-62fg-b66d-g29ge3h12cg0"
}
```

**Поля:**
| Поле | Тип | Обязательное | Описание |
|------|-----|--------------|----------|
| `deliveryType` | integer | Да | Тип доставки: `1` - Самовывоз, `2` - Транспортная компания, `3` - Доставка магазином |
| `deliveryAddressId` | GUID (string) | Нет | Идентификатор адреса доставки (обязателен если не самовывоз) |
| `cargoReceiverId` | GUID (string) | Нет | Идентификатор грузополучателя |
| `carrierId` | GUID (string) | Нет | Идентификатор транспортной компании (обязателен для типа доставки "Транспортная компания") |

**Ответ (201 Created):**
```json
{
  "id": "1a2b3c4d-5e6f-7a8b-9c0d-1e2f3a4b5c6d",
  "orderNumber": "ORD-2024-001",
  "status": 1,
  "statusName": "Обрабатывается",
  "deliveryType": 1,
  "trackingNumber": null,
  "totalAmount": 2500.00,
  "createdAt": "2024-12-07T10:30:00Z",
  "items": [
    {
      "id": "2b3c4d5e-6f7a-8b9c-0d1e-2f3a4b5c6d7e",
      "nomenclatureId": "550e8400-e29b-41d4-a716-446655440000",
      "nomenclatureName": "Товар 1",
      "quantity": 2,
      "price": 1000.00,
      "discountPercent": 10.0,
      "totalAmount": 1800.00
    }
  ]
}
```

**Ошибки:**
- `400 Bad Request` - корзина пуста, неверные данные или отсутствуют обязательные поля для выбранного типа доставки
- `401 Unauthorized` - пользователь не авторизован
- `500 Internal Server Error` - внутренняя ошибка сервера

**Пример запроса:**
```typescript
const orderRequest = {
  deliveryType: 2, // Транспортная компания
  deliveryAddressId: "8d9e6679-7425-40de-944b-e07fc1f90ae8",
  cargoReceiverId: "9e0f7780-8536-51ef-a55c-f18fd2g01bf9",
  carrierId: "0f1g8891-9647-62fg-b66d-g29ge3h12cg0"
};

const response = await fetch('https://test.fimbiz.ru/api/cart/create-order', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${accessToken}`,
    'Content-Type': 'application/json'
  },
  body: JSON.stringify(orderRequest)
});

const order = await response.json();
```

---

## Типы данных

### DeliveryType (enum)

| Значение | Название | Описание |
|----------|----------|----------|
| `1` | SelfPickup | Самовывоз |
| `2` | Carrier | Транспортная компания |
| `3` | StoreDelivery | Доставка магазином |

### OrderStatus (enum)

| Значение | Название | Описание |
|----------|----------|----------|
| `1` | Processing | Обрабатывается |
| `2` | AwaitingPayment | Ожидает оплаты |
| `3` | InvoiceConfirmed | Счет подтвержден |
| `4` | Manufacturing | Изготавливается |
| `5` | Assembling | Собирается |
| `6` | TransferredToCarrier | Передается в транспортную компанию |
| `7` | DeliveringByCarrier | Доставляется транспортной компанией |
| `8` | Delivering | Доставляется |
| `9` | AwaitingPickup | Ожидает получения |
| `10` | Received | Получен |

---

## Обработка ошибок

Все ошибки возвращаются в формате:

```json
{
  "error": "Описание ошибки"
}
```

**HTTP коды:**
- `400 Bad Request` - неверные данные запроса
- `401 Unauthorized` - пользователь не авторизован или токен недействителен
- `404 Not Found` - ресурс не найден
- `500 Internal Server Error` - внутренняя ошибка сервера

---

## Важные замечания

1. **Автоматическое применение скидок**: При получении корзины автоматически применяются все активные скидки контрагента. Поля `discountPercent`, `priceWithDiscount` и `totalAmount` уже содержат расчеты со скидками.

2. **Объединение товаров**: Если вы добавляете товар с `nomenclatureId`, который уже есть в корзине, количество увеличивается, а не создается новая позиция.

3. **ID товара vs ID позиции**: Важно различать:
   - `nomenclatureId` - идентификатор товара в системе (одинаковый для всех экземпляров)
   - `itemId` (или `id` в `CartItemDto`) - идентификатор позиции в корзине (уникальный для каждой позиции)

4. **Очистка корзины после заказа**: После успешного создания заказа корзина автоматически очищается.

5. **Валидация данных**: Все обязательные поля проверяются на сервере. Убедитесь, что отправляете корректные данные.

---

## Примеры использования (TypeScript/React)

### React Hook для работы с корзиной

```typescript
import { useState, useEffect } from 'react';

interface CartItem {
  id: string;
  nomenclatureId: string;
  nomenclatureName: string;
  quantity: number;
  price: number;
  priceWithDiscount: number;
  totalAmount: number;
  urlPhotos: string[];
}

interface Cart {
  id: string;
  items: CartItem[];
  totalAmount: number;
}

const API_BASE_URL = 'https://test.fimbiz.ru/api';

export const useCart = (accessToken: string) => {
  const [cart, setCart] = useState<Cart | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchCart = async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetch(`${API_BASE_URL}/cart`, {
        headers: {
          'Authorization': `Bearer ${accessToken}`,
          'Content-Type': 'application/json'
        }
      });
      
      if (!response.ok) {
        throw new Error('Ошибка загрузки корзины');
      }
      
      const data = await response.json();
      setCart(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Неизвестная ошибка');
    } finally {
      setLoading(false);
    }
  };

  const addToCart = async (item: {
    nomenclatureId: string;
    nomenclatureName: string;
    quantity: number;
    price: number;
    unitType?: string;
    sku?: string;
    urlPhotos?: string[];
  }) => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetch(`${API_BASE_URL}/cart/add`, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${accessToken}`,
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(item)
      });
      
      if (!response.ok) {
        const errorData = await response.json();
        throw new Error(errorData.error || 'Ошибка добавления товара');
      }
      
      const updatedCart = await response.json();
      setCart(updatedCart);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Неизвестная ошибка');
    } finally {
      setLoading(false);
    }
  };

  const updateQuantity = async (itemId: string, quantity: number) => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetch(`${API_BASE_URL}/cart/${itemId}`, {
        method: 'PUT',
        headers: {
          'Authorization': `Bearer ${accessToken}`,
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({ quantity })
      });
      
      if (!response.ok) {
        throw new Error('Ошибка обновления количества');
      }
      
      const updatedCart = await response.json();
      setCart(updatedCart);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Неизвестная ошибка');
    } finally {
      setLoading(false);
    }
  };

  const removeFromCart = async (itemId: string) => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetch(`${API_BASE_URL}/cart/${itemId}`, {
        method: 'DELETE',
        headers: {
          'Authorization': `Bearer ${accessToken}`
        }
      });
      
      if (!response.ok) {
        throw new Error('Ошибка удаления товара');
      }
      
      // Перезагружаем корзину
      await fetchCart();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Неизвестная ошибка');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (accessToken) {
      fetchCart();
    }
  }, [accessToken]);

  return {
    cart,
    loading,
    error,
    fetchCart,
    addToCart,
    updateQuantity,
    removeFromCart
  };
};
```

### Использование в компоненте

```typescript
import React from 'react';
import { useCart } from './hooks/useCart';

const CartComponent: React.FC<{ accessToken: string }> = ({ accessToken }) => {
  const { cart, loading, error, addToCart, updateQuantity, removeFromCart } = useCart(accessToken);

  const handleAddProduct = () => {
    addToCart({
      nomenclatureId: '550e8400-e29b-41d4-a716-446655440000',
      nomenclatureName: 'Пример товара',
      quantity: 1,
      price: 1000.00,
      unitType: 'шт',
      urlPhotos: ['https://example.com/photo.jpg']
    });
  };

  if (loading) return <div>Загрузка...</div>;
  if (error) return <div>Ошибка: {error}</div>;
  if (!cart) return <div>Корзина пуста</div>;

  return (
    <div>
      <h2>Корзина</h2>
      {cart.items.map(item => (
        <div key={item.id}>
          <h3>{item.nomenclatureName}</h3>
          <p>Цена: {item.priceWithDiscount} руб.</p>
          <p>Количество: {item.quantity}</p>
          <button onClick={() => updateQuantity(item.id, item.quantity + 1)}>
            +
          </button>
          <button onClick={() => updateQuantity(item.id, item.quantity - 1)}>
            -
          </button>
          <button onClick={() => removeFromCart(item.id)}>
            Удалить
          </button>
        </div>
      ))}
      <p>Итого: {cart.totalAmount} руб.</p>
    </div>
  );
};
```

---

## Дополнительные ресурсы

- Swagger документация: `https://test.fimbiz.ru/swagger`
- API авторизации: см. документацию по авторизации
- API заказов: см. документацию по заказам


