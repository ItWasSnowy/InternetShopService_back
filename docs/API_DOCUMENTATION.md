# API Документация для фронтенда

## Базовый URL
```
https://your-api-domain.com/api
```

## Аутентификация

API использует JWT (JSON Web Token) для аутентификации. После успешной авторизации вы получите `accessToken` и `refreshToken`.

### Использование токена

Все защищенные эндпоинты требуют заголовок `Authorization`:
```
Authorization: Bearer {accessToken}
```

### Время жизни токенов

- **accessToken**: 60 минут (настраивается через `JwtSettings:ExpirationMinutes`)
- **refreshToken**: 24 часа

При истечении `accessToken` используйте эндпоинт `/api/auth/refresh` для получения нового токена без повторной авторизации.

---

## 1. Авторизация (`/api/auth`)

### 1.1. Запрос кода для входа по звонку

**POST** `/api/auth/request-code`

Запрашивает звонок с кодом подтверждения на указанный номер телефона.

**Тело запроса:**
```json
{
  "phoneNumber": "79991234567"
}
```

**Параметры:**
- `phoneNumber` (string, required) - Номер телефона в формате `7XXXXXXXXXX` (11 цифр, начинается с 7)

**Успешный ответ (200 OK):**
```json
{
  "message": "Код подтверждения отправлен"
}
```

**Ошибки:**
- `400 Bad Request` - Неверный формат номера телефона или контрагент не найден в FimBiz
- `500 Internal Server Error` - Внутренняя ошибка сервера

**Пример:**
```javascript
const response = await fetch('/api/auth/request-code', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json'
  },
  body: JSON.stringify({
    phoneNumber: '79991234567'
  })
});
```

---

### 1.2. Проверка кода подтверждения

**POST** `/api/auth/verify-code`

Проверяет код подтверждения и возвращает JWT токены.

**Тело запроса:**
```json
{
  "phoneNumber": "79991234567",
  "code": "1234"
}
```

**Параметры:**
- `phoneNumber` (string, required) - Номер телефона
- `code` (string, required) - 4-значный код из звонка

**Успешный ответ (200 OK):**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "requiresPasswordSetup": true,
  "user": {
    "id": "123e4567-e89b-12d3-a456-426614174000",
    "phoneNumber": "79991234567",
    "counterpartyId": "123e4567-e89b-12d3-a456-426614174001"
  }
}
```

**Поля ответа:**
- `accessToken` - JWT токен для авторизации (используйте в заголовке Authorization). Время жизни: 60 минут (настраивается через `JwtSettings:ExpirationMinutes`)
- `refreshToken` - Токен для обновления accessToken. Время жизни: 24 часа. Используйте для обновления accessToken через `/api/auth/refresh`
- `requiresPasswordSetup` - `true` если нужно установить пароль
- `user` - Информация о пользователе

**Ошибки:**
- `401 Unauthorized` - Неверный код подтверждения
- `400 Bad Request` - Истекло время действия кода или пользователь не найден
- `500 Internal Server Error` - Внутренняя ошибка сервера

**Пример:**
```javascript
const response = await fetch('/api/auth/verify-code', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json'
  },
  body: JSON.stringify({
    phoneNumber: '79991234567',
    code: '1234'
  })
});

const data = await response.json();
// Сохраните токены
localStorage.setItem('accessToken', data.accessToken);
localStorage.setItem('refreshToken', data.refreshToken);
```

---

### 1.3. Установка пароля

**POST** `/api/auth/set-password`

**Требует авторизации:** Да

Устанавливает пароль для пользователя. Номер телефона берется из JWT токена.

**Тело запроса:**
```json
{
  "password": "mypassword123"
}
```

**Параметры:**
- `password` (string, required) - Пароль (минимум 6 символов)

**Успешный ответ (200 OK):**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "requiresPasswordSetup": false,
  "user": {
    "id": "123e4567-e89b-12d3-a456-426614174000",
    "phoneNumber": "79991234567",
    "counterpartyId": "123e4567-e89b-12d3-a456-426614174001"
  }
}
```

**Ошибки:**
- `400 Bad Request` - Пароль слишком короткий или пользователь не найден
- `401 Unauthorized` - Токен недействителен
- `500 Internal Server Error` - Внутренняя ошибка сервера

---

### 1.4. Смена пароля

**POST** `/api/auth/change-password`

**Требует авторизации:** Да

Изменяет пароль пользователя. Требует указания текущего пароля для подтверждения.

**Заголовки:**
```
Authorization: Bearer {accessToken}
Content-Type: application/json
```

**Тело запроса:**
```json
{
  "currentPassword": "старый_пароль123",
  "newPassword": "новый_пароль456"
}
```

**Параметры:**

| Параметр | Тип | Обязательный | Описание |
|----------|-----|--------------|----------|
| `currentPassword` | `string` | ✅ Да | Текущий пароль пользователя |
| `newPassword` | `string` | ✅ Да | Новый пароль (минимум 6 символов) |

**Успешный ответ (200 OK):**
```json
{
  "message": "Пароль успешно изменен"
}
```

**Ошибки:**

| Код | Описание |
|-----|----------|
| `400 Bad Request` | Неверный формат данных, новый пароль слишком короткий (менее 6 символов), новый пароль совпадает с текущим |
| `401 Unauthorized` | Пользователь не авторизован, токен недействителен или неверный текущий пароль |
| `500 Internal Server Error` | Внутренняя ошибка сервера |

**Важные замечания:**

1. **Текущий пароль обязателен** - Для безопасности требуется указать текущий пароль
2. **Минимальная длина** - Новый пароль должен содержать минимум 6 символов
3. **Пароль должен отличаться** - Новый пароль должен отличаться от текущего
4. **Пароль должен быть установлен** - Если пароль не был установлен ранее, используйте `/api/auth/set-password` вместо смены пароля
5. **ID пользователя** - Автоматически определяется из JWT токена, не требуется передавать в запросе

**Пример использования:**

```javascript
const response = await fetch('/api/auth/change-password', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'Authorization': `Bearer ${accessToken}`
  },
  body: JSON.stringify({
    currentPassword: 'старый_пароль123',
    newPassword: 'новый_пароль456'
  })
});

if (response.ok) {
  const data = await response.json();
  console.log(data.message); // "Пароль успешно изменен"
} else {
  const error = await response.json();
  console.error(error.error);
}
```

**Пример с обработкой ошибок:**

```javascript
async function changePassword(currentPassword, newPassword) {
  try {
    const response = await fetch('/api/auth/change-password', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${accessToken}`
      },
      body: JSON.stringify({
        currentPassword: currentPassword,
        newPassword: newPassword
      })
    });

    if (!response.ok) {
      const error = await response.json();
      
      if (response.status === 401) {
        // Неверный текущий пароль или токен истек
        throw new Error(error.error || 'Неверный пароль или токен истек');
      } else if (response.status === 400) {
        // Неверный формат или валидация не прошла
        throw new Error(error.error || 'Ошибка валидации');
      }
      
      throw new Error('Ошибка при смене пароля');
    }

    const data = await response.json();
    return data.message;
  } catch (error) {
    console.error('Ошибка смены пароля:', error);
    throw error;
  }
}

// Использование
try {
  await changePassword('старый_пароль', 'новый_пароль123');
  alert('Пароль успешно изменен');
} catch (error) {
  alert(error.message);
}
```

---

### 1.5. Вход по паролю

**POST** `/api/auth/login`

Вход в систему используя номер телефона и пароль.

**Тело запроса:**
```json
{
  "phoneNumber": "79991234567",
  "password": "mypassword123"
}
```

**Параметры:**
- `phoneNumber` (string, required) - Номер телефона
- `password` (string, required) - Пароль

**Успешный ответ (200 OK):**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "requiresPasswordSetup": false,
  "user": {
    "id": "123e4567-e89b-12d3-a456-426614174000",
    "phoneNumber": "79991234567",
    "counterpartyId": "123e4567-e89b-12d3-a456-426614174001"
  }
}
```

**Ошибки:**
- `401 Unauthorized` - Неверный номер телефона или пароль
- `400 Bad Request` - Пароль не установлен (используйте вход по звонку)
- `500 Internal Server Error` - Внутренняя ошибка сервера

---

### 1.5. Обновление токена

**POST** `/api/auth/refresh`

Обновляет accessToken используя refreshToken. Используйте этот эндпоинт когда accessToken истек.

**Тело запроса:**
```json
{
  "refreshToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Параметры:**
- `refreshToken` (string, required) - Refresh token, полученный при авторизации

**Успешный ответ (200 OK):**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "requiresPasswordSetup": false,
  "user": {
    "id": "123e4567-e89b-12d3-a456-426614174000",
    "phoneNumber": "79991234567",
    "counterpartyId": "123e4567-e89b-12d3-a456-426614174001"
  }
}
```

**Важно:** После обновления токена вы получите новый `accessToken` и новый `refreshToken`. Обязательно сохраните новый `refreshToken`, так как старый больше не будет работать.

**Ошибки:**
- `400 Bad Request` - Refresh token не предоставлен
- `401 Unauthorized` - Неверный или истекший refresh token
- `500 Internal Server Error` - Внутренняя ошибка сервера

**Пример:**
```javascript
// Когда accessToken истек (получили 401), обновляем токен
const refreshResponse = await fetch('/api/auth/refresh', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json'
  },
  body: JSON.stringify({
    refreshToken: localStorage.getItem('refreshToken')
  })
});

if (refreshResponse.ok) {
  const data = await refreshResponse.json();
  // Сохраняем новые токены
  localStorage.setItem('accessToken', data.accessToken);
  localStorage.setItem('refreshToken', data.refreshToken);
  
  // Повторяем оригинальный запрос с новым токеном
} else {
  // Refresh token истек - требуется повторная авторизация
  // Перенаправляем на страницу входа
}
```

---

### 1.6. Выход из системы

**POST** `/api/auth/logout`

**Требует авторизации:** Да

Деактивирует текущую сессию пользователя.

**Успешный ответ (200 OK):**
```json
{
  "message": "Выход выполнен успешно"
}
```

**Ошибки:**
- `401 Unauthorized` - Токен не предоставлен или недействителен
- `500 Internal Server Error` - Внутренняя ошибка сервера

---

## 2. Корзина (`/api/cart`)

Все эндпоинты корзины требуют авторизации.

### 2.1. Получить корзину

**GET** `/api/cart`

Возвращает текущую корзину пользователя с примененными скидками.

**Успешный ответ (200 OK):**
```json
{
  "id": "123e4567-e89b-12d3-a456-426614174000",
  "items": [
    {
      "id": "123e4567-e89b-12d3-a456-426614174001",
      "nomenclatureId": "123e4567-e89b-12d3-a456-426614174002",
      "nomenclatureName": "Товар 1",
      "quantity": 2,
      "price": 1000.00,
      "discountPercent": 10.0,
      "priceWithDiscount": 900.00,
      "totalAmount": 1800.00
    }
  ],
  "totalAmount": 1800.00
}
```

**Поля ответа:**
- `id` - ID корзины
- `items` - Массив товаров в корзине
  - `id` - ID позиции в корзине
  - `nomenclatureId` - ID номенклатуры
  - `nomenclatureName` - Название товара
  - `quantity` - Количество
  - `price` - Цена за единицу (без скидки)
  - `discountPercent` - Процент скидки
  - `priceWithDiscount` - Цена за единицу со скидкой
  - `totalAmount` - Итоговая сумма позиции (с учетом скидки)
- `totalAmount` - Общая сумма корзины

---

### 2.2. Добавить товар в корзину

**POST** `/api/cart/add`

Добавляет товар в корзину или увеличивает количество, если товар уже есть.

**Тело запроса:**
```json
{
  "nomenclatureId": "123e4567-e89b-12d3-a456-426614174002",
  "nomenclatureName": "Товар 1",
  "quantity": 2,
  "price": 1000.00
}
```

**Параметры:**
- `nomenclatureId` (Guid, required) - ID номенклатуры
- `nomenclatureName` (string, required) - Название товара
- `quantity` (int, required) - Количество (должно быть > 0)
- `price` (decimal, required) - Цена за единицу

**Успешный ответ (200 OK):**
Возвращает обновленную корзину (формат как в GET `/api/cart`)

**Ошибки:**
- `400 Bad Request` - Неверные параметры или корзина не найдена
- `401 Unauthorized` - Пользователь не авторизован

---

### 2.3. Обновить количество товара

**PUT** `/api/cart/{itemId}`

Обновляет количество товара в корзине.

**Параметры URL:**
- `itemId` (Guid) - ID позиции в корзине

**Тело запроса:**
```json
{
  "quantity": 5
}
```

**Параметры:**
- `quantity` (int, required) - Новое количество (должно быть > 0)

**Успешный ответ (200 OK):**
Возвращает обновленную корзину

**Ошибки:**
- `400 Bad Request` - Неверное количество или позиция не найдена
- `401 Unauthorized` - Позиция не принадлежит пользователю
- `404 Not Found` - Позиция не найдена

---

### 2.4. Удалить товар из корзины

**DELETE** `/api/cart/{itemId}`

Удаляет товар из корзины.

**Параметры URL:**
- `itemId` (Guid) - ID позиции в корзине

**Успешный ответ (204 No Content)**

**Ошибки:**
- `401 Unauthorized` - Позиция не принадлежит пользователю
- `404 Not Found` - Позиция не найдена

---

### 2.5. Очистить корзину

**DELETE** `/api/cart/clear`

Удаляет все товары из корзины.

**Успешный ответ (204 No Content)**

**Ошибки:**
- `404 Not Found` - Корзина не найдена

---

### 2.6. Создать заказ из корзины

**POST** `/api/cart/create-order`

Создает заказ из товаров в корзине. После создания заказа корзина очищается.

**Тело запроса:**
```json
{
  "deliveryType": "SelfPickup",
  "deliveryAddressId": "123e4567-e89b-12d3-a456-426614174003",
  "cargoReceiverId": "123e4567-e89b-12d3-a456-426614174004",
  "carrierId": "123e4567-e89b-12d3-a456-426614174005"
}
```

**Параметры:**
- `deliveryType` (string, required) - Тип доставки:
  - `SelfPickup` - Самовывоз
  - `Carrier` - Транспортная компания
  - `ShopDelivery` - Доставка магазином
- `deliveryAddressId` (Guid, optional) - ID адреса доставки (обязателен для Carrier и ShopDelivery)
- `cargoReceiverId` (Guid, optional) - ID грузополучателя
- `carrierId` (Guid, optional) - ID транспортной компании (обязателен для Carrier)

**Успешный ответ (201 Created):**
```json
{
  "id": "123e4567-e89b-12d3-a456-426614174006",
  "orderNumber": "ORD-2024-001",
  "status": "Processing",
  "statusName": "Обрабатывается",
  "deliveryType": "SelfPickup",
  "totalAmount": 1800.00,
  "createdAt": "2024-01-15T10:30:00Z",
  "items": [...],
  "deliveryAddress": {...},
  "cargoReceiver": {...}
}
```

**Ошибки:**
- `400 Bad Request` - Корзина пуста, адрес/грузополучатель не найден или неверные параметры
- `401 Unauthorized` - Пользователь не авторизован

---

## 3. Адреса доставки (`/api/deliveryaddress`)

Все эндпоинты требуют авторизации.

### 3.1. Получить все адреса

**GET** `/api/deliveryaddress`

Возвращает список всех адресов доставки пользователя.

**Успешный ответ (200 OK):**
```json
[
  {
    "id": "123e4567-e89b-12d3-a456-426614174003",
    "address": "ул. Ленина, д. 10, кв. 5",
    "city": "Москва",
    "region": "Московская область",
    "postalCode": "123456",
    "apartment": "5",
    "isDefault": true
  }
]
```

---

### 3.2. Получить адрес по ID

**GET** `/api/deliveryaddress/{id}`

**Параметры URL:**
- `id` (Guid) - ID адреса

**Успешный ответ (200 OK):**
Возвращает объект адреса (формат как в списке)

**Ошибки:**
- `404 Not Found` - Адрес не найден

---

### 3.3. Получить адрес по умолчанию

**GET** `/api/deliveryaddress/default`

Возвращает адрес доставки, помеченный как адрес по умолчанию.

**Успешный ответ (200 OK):**
Возвращает объект адреса или `404 Not Found` если адрес по умолчанию не установлен

---

### 3.4. Создать адрес

**POST** `/api/deliveryaddress`

**Тело запроса:**
```json
{
  "address": "ул. Ленина, д. 10, кв. 5",
  "city": "Москва",
  "region": "Московская область",
  "postalCode": "123456",
  "apartment": "5",
  "isDefault": false
}
```

**Параметры:**
- `address` (string, required) - Адрес
- `city` (string, optional) - Город
- `region` (string, optional) - Регион/область
- `postalCode` (string, optional) - Почтовый индекс
- `apartment` (string, optional) - Квартира/офис
- `isDefault` (bool, optional) - Установить как адрес по умолчанию

**Успешный ответ (201 Created):**
Возвращает созданный адрес с ID

---

### 3.5. Обновить адрес

**PUT** `/api/deliveryaddress/{id}`

**Параметры URL:**
- `id` (Guid) - ID адреса

**Тело запроса:** (те же поля, что и при создании)

**Успешный ответ (200 OK):**
Возвращает обновленный адрес

**Ошибки:**
- `400 Bad Request` - Неверные параметры
- `404 Not Found` - Адрес не найден

---

### 3.6. Удалить адрес

**DELETE** `/api/deliveryaddress/{id}`

**Параметры URL:**
- `id` (Guid) - ID адреса

**Успешный ответ (204 No Content)**

**Ошибки:**
- `404 Not Found` - Адрес не найден

---

### 3.7. Установить адрес по умолчанию

**PUT** `/api/deliveryaddress/{id}/set-default`

**Параметры URL:**
- `id` (Guid) - ID адреса

**Успешный ответ (200 OK):**
Возвращает обновленный адрес (теперь `isDefault: true`)

**Ошибки:**
- `400 Bad Request` - Адрес не найден
- `404 Not Found` - Адрес не найден

---

## 4. Грузополучатели (`/api/cargoreceiver`)

Все эндпоинты требуют авторизации.

### 4.1. Получить всех грузополучателей

**GET** `/api/cargoreceiver`

**Успешный ответ (200 OK):**
```json
[
  {
    "id": "123e4567-e89b-12d3-a456-426614174004",
    "fullName": "Иванов Иван Иванович",
    "passportSeries": "1234",
    "passportNumber": "567890",
    "passportIssuedBy": "УФМС России по г. Москве",
    "passportIssueDate": "2020-01-15",
    "isDefault": true
  }
]
```

---

### 4.2. Получить грузополучателя по ID

**GET** `/api/cargoreceiver/{id}`

**Параметры URL:**
- `id` (Guid) - ID грузополучателя

---

### 4.3. Получить грузополучателя по умолчанию

**GET** `/api/cargoreceiver/default`

---

### 4.4. Создать грузополучателя

**POST** `/api/cargoreceiver`

**Тело запроса:**
```json
{
  "fullName": "Иванов Иван Иванович",
  "passportSeries": "1234",
  "passportNumber": "567890",
  "passportIssuedBy": "УФМС России по г. Москве",
  "passportIssueDate": "2020-01-15",
  "isDefault": false
}
```

**Параметры:**
- `fullName` (string, required) - ФИО
- `passportSeries` (string, required) - Серия паспорта
- `passportNumber` (string, required) - Номер паспорта
- `passportIssuedBy` (string, optional) - Кем выдан
- `passportIssueDate` (string, optional) - Дата выдачи (формат: YYYY-MM-DD)
- `isDefault` (bool, optional) - Установить как получателя по умолчанию

---

### 4.5. Обновить грузополучателя

**PUT** `/api/cargoreceiver/{id}`

**Параметры URL:**
- `id` (Guid) - ID грузополучателя

**Тело запроса:** (те же поля, что и при создании)

---

### 4.6. Удалить грузополучателя

**DELETE** `/api/cargoreceiver/{id}`

---

### 4.7. Установить грузополучателя по умолчанию

**PUT** `/api/cargoreceiver/{id}/set-default`

---

## 5. Контрагент (`/api/counterparty`)

Все эндпоинты требуют авторизации.

### 5.1. Получить данные текущего контрагента

**GET** `/api/counterparty/current`

**Успешный ответ (200 OK):**
```json
{
  "id": "123e4567-e89b-12d3-a456-426614174001",
  "name": "ООО Рога и Копыта",
  "phoneNumber": "79991234567",
  "email": "info@example.com",
  "inn": "1234567890",
  "kpp": "123456789",
  "legalAddress": "г. Москва, ул. Ленина, д. 1",
  "type": "B2B"
}
```

**Поля ответа:**
- `id` - ID контрагента
- `name` - Наименование
- `phoneNumber` - Номер телефона
- `email` - Email
- `inn` - ИНН
- `kpp` - КПП
- `legalAddress` - Юридический адрес
- `type` - Тип контрагента (`B2B` или `B2C`)

---

### 5.2. Получить скидки контрагента

**GET** `/api/counterparty/discounts`

**Успешный ответ (200 OK):**
```json
[
  {
    "id": "123e4567-e89b-12d3-a456-426614174007",
    "nomenclatureGroupId": "123e4567-e89b-12d3-a456-426614174008",
    "nomenclatureId": null,
    "discountPercent": 15.0,
    "validFrom": "2024-01-01T00:00:00Z",
    "validTo": "2024-12-31T23:59:59Z",
    "isActive": true
  }
]
```

**Поля ответа:**
- `id` - ID скидки
- `nomenclatureGroupId` - ID группы номенклатуры (если скидка на группу)
- `nomenclatureId` - ID номенклатуры (если скидка на конкретный товар)
- `discountPercent` - Процент скидки
- `validFrom` - Дата начала действия
- `validTo` - Дата окончания действия (может быть null)
- `isActive` - Активна ли скидка

---

### 5.3. Синхронизировать данные контрагента

**POST** `/api/counterparty/sync`

Принудительно синхронизирует данные контрагента с FimBiz.

**Успешный ответ (200 OK):**
```json
{
  "message": "Данные синхронизированы успешно"
}
```

---

## 6. Сессии (`/api/sessions`)

Все эндпоинты требуют авторизации.

### 6.1. Получить список активных сессий

**GET** `/api/sessions`

Возвращает список всех активных сессий текущего пользователя.

**Успешный ответ (200 OK):**
```json
[
  {
    "id": "123e4567-e89b-12d3-a456-426614174009",
    "createdAt": "2024-01-15T10:30:00Z",
    "expiresAt": "2024-01-16T10:30:00Z",
    "isActive": true,
    "deviceInfo": "Chrome on Windows",
    "userAgent": "Mozilla/5.0...",
    "ipAddress": "192.168.1.1",
    "deviceName": null,
    "isCurrentSession": true
  }
]
```

**Поля ответа:**
- `id` - ID сессии
- `createdAt` - Дата создания
- `expiresAt` - Дата истечения
- `isActive` - Активна ли сессия
- `deviceInfo` - Информация об устройстве (например, "Chrome on Windows")
- `userAgent` - User-Agent браузера
- `ipAddress` - IP адрес входа
- `deviceName` - Название устройства (если указано)
- `isCurrentSession` - Является ли это текущей сессией

---

### 6.2. Деактивировать сессию

**POST** `/api/sessions/{sessionId}/deactivate`

**Параметры URL:**
- `sessionId` (Guid) - ID сессии

**Успешный ответ (200 OK):**
```json
{
  "message": "Сессия успешно деактивирована"
}
```

**Ошибки:**
- `404 Not Found` - Сессия не найдена или не принадлежит пользователю

---

### 6.3. Деактивировать несколько сессий

**POST** `/api/sessions/deactivate`

**Тело запроса:**
```json
{
  "sessionIds": [
    "123e4567-e89b-12d3-a456-426614174009",
    "123e4567-e89b-12d3-a456-426614174010"
  ]
}
```

**Параметры:**
- `sessionIds` (Guid[], required) - Массив ID сессий

**Успешный ответ (200 OK):**
```json
{
  "message": "Сессии успешно деактивированы"
}
```

---

## Коды ошибок

- `200 OK` - Успешный запрос
- `201 Created` - Ресурс успешно создан
- `204 No Content` - Успешный запрос без тела ответа
- `400 Bad Request` - Неверные параметры запроса
- `401 Unauthorized` - Требуется авторизация или неверные учетные данные
- `404 Not Found` - Ресурс не найден
- `500 Internal Server Error` - Внутренняя ошибка сервера

---

## Примеры использования

### Полный цикл авторизации и работы с корзиной

```javascript
// 1. Запрос кода
const requestCodeResponse = await fetch('/api/auth/request-code', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ phoneNumber: '79991234567' })
});

// 2. Проверка кода (пользователь вводит код из звонка)
const verifyCodeResponse = await fetch('/api/auth/verify-code', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ 
    phoneNumber: '79991234567',
    code: '1234'
  })
});

const authData = await verifyCodeResponse.json();
const accessToken = authData.accessToken;

// 3. Установка пароля (если requiresPasswordSetup === true)
if (authData.requiresPasswordSetup) {
  await fetch('/api/auth/set-password', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${accessToken}`
    },
    body: JSON.stringify({ password: 'mypassword123' })
  });
}

// 4. Получение корзины
const cartResponse = await fetch('/api/cart', {
  headers: {
    'Authorization': `Bearer ${accessToken}`
  }
});
const cart = await cartResponse.json();

// 5. Добавление товара в корзину
await fetch('/api/cart/add', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'Authorization': `Bearer ${accessToken}`
  },
  body: JSON.stringify({
    nomenclatureId: '123e4567-e89b-12d3-a456-426614174002',
    nomenclatureName: 'Товар 1',
    quantity: 2,
    price: 1000.00
  })
});

// 6. Обработка истечения токена (пример перехватчика запросов)
const originalFetch = window.fetch;
window.fetch = async function(...args) {
  let response = await originalFetch(...args);
  
  // Если получили 401, пытаемся обновить токен
  if (response.status === 401) {
    const refreshToken = localStorage.getItem('refreshToken');
    if (refreshToken) {
      const refreshResponse = await originalFetch('/api/auth/refresh', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken })
      });
      
      if (refreshResponse.ok) {
        const data = await refreshResponse.json();
        localStorage.setItem('accessToken', data.accessToken);
        localStorage.setItem('refreshToken', data.refreshToken);
        
        // Повторяем оригинальный запрос с новым токеном
        const newHeaders = new Headers(args[1]?.headers);
        newHeaders.set('Authorization', `Bearer ${data.accessToken}`);
        args[1] = { ...args[1], headers: newHeaders };
        response = await originalFetch(...args);
      } else {
        // Refresh token истек - перенаправляем на страницу входа
        window.location.href = '/login';
        return response;
      }
    }
  }
  
  return response;
};

// 7. Создание заказа
const orderResponse = await fetch('/api/cart/create-order', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'Authorization': `Bearer ${accessToken}`
  },
  body: JSON.stringify({
    deliveryType: 'SelfPickup',
    deliveryAddressId: '123e4567-e89b-12d3-a456-426614174003',
    cargoReceiverId: '123e4567-e89b-12d3-a456-426614174004'
  })
});
const order = await orderResponse.json();
```

---

## Примечания

1. **Формат номера телефона**: Всегда используйте формат `7XXXXXXXXXX` (11 цифр, начинается с 7)

2. **JWT токены**: 
   - `accessToken` живет 60 минут (настраивается через `JwtSettings:ExpirationMinutes`)
   - `refreshToken` живет 24 часа
   - При истечении `accessToken` используйте `/api/auth/refresh` для получения нового токена
   - При истечении `refreshToken` пользователь должен повторно авторизоваться

3. **Скидки**: Скидки применяются автоматически при расчете суммы корзины. Скидки могут быть на группу номенклатуры или на конкретный товар.

4. **Типы доставки**:
   - `SelfPickup` - Самовывоз (не требует адреса)
   - `Carrier` - Транспортная компания (требует адрес и carrierId)
   - `ShopDelivery` - Доставка магазином (требует адрес)

5. **Сессии**: Каждый вход создает новую сессию. Старые сессии могут быть деактивированы автоматически или вручную.

6. **Синхронизация**: Данные контрагента и скидки синхронизируются автоматически с FimBiz, но можно принудительно запустить синхронизацию через `/api/counterparty/sync`.

