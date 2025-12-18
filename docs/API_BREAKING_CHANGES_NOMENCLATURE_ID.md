# Breaking Changes: NomenclatureId Type Change

## Обзор

**Дата изменения:** 18.12.2024  
**Тип изменения:** Breaking Change  
**Причина:** Унификация типов данных с FimBiz (номенклатуры хранятся как `int`, а не `Guid`)

## Изменения

Тип поля `nomenclatureId` изменен с `Guid` (string) на `int` (number) во всех API ответах и запросах, где используется ID номенклатуры.

## Затронутые эндпоинты

### 1. Заказы (Orders)

#### GET `/api/orders/{id}` 
**Ответ:** `OrderDto`

**Изменение:**
```json
// БЫЛО
{
  "items": [
    {
      "id": "guid",
      "nomenclatureId": "00000000-0000-0000-0000-000000000031",  // string (Guid)
      "nomenclatureName": "Товар",
      ...
    }
  ]
}

// СТАЛО
{
  "items": [
    {
      "id": "guid",
      "nomenclatureId": 31,  // number (int)
      "nomenclatureName": "Товар",
      ...
    }
  ]
}
```

#### POST `/api/orders`
**Тело запроса:** `CreateOrderDto`

**Изменение:**
```json
// БЫЛО
{
  "items": [
    {
      "nomenclatureId": "00000000-0000-0000-0000-000000000031",  // string (Guid)
      "nomenclatureName": "Товар",
      "quantity": 1,
      "price": 1000.00
    }
  ]
}

// СТАЛО
{
  "items": [
    {
      "nomenclatureId": 31,  // number (int)
      "nomenclatureName": "Товар",
      "quantity": 1,
      "price": 1000.00
    }
  ]
}
```

### 2. Корзина (Cart)

#### GET `/api/cart`
**Ответ:** `CartDto`

**Изменение:**
```json
// БЫЛО
{
  "items": [
    {
      "id": "guid",
      "nomenclatureId": "00000000-0000-0000-0000-000000000031",  // string (Guid)
      "nomenclatureName": "Товар",
      ...
    }
  ]
}

// СТАЛО
{
  "items": [
    {
      "id": "guid",
      "nomenclatureId": 31,  // number (int)
      "nomenclatureName": "Товар",
      ...
    }
  ]
}
```

#### POST `/api/cart/items`
**Тело запроса:** `AddCartItemDto`

**Изменение:**
```json
// БЫЛО
{
  "nomenclatureId": "00000000-0000-0000-0000-000000000031",  // string (Guid)
  "nomenclatureName": "Товар",
  "quantity": 1,
  "price": 1000.00
}

// СТАЛО
{
  "nomenclatureId": 31,  // number (int)
  "nomenclatureName": "Товар",
  "quantity": 1,
  "price": 1000.00
}
```

### 3. Скидки контрагента (Counterparty Discounts)

#### GET `/api/counterparty/discounts`
**Ответ:** `DiscountDto[]`

**Изменение:**
```json
// БЫЛО
[
  {
    "id": "guid",
    "nomenclatureGroupId": 5,  // number (int) или null
    "nomenclatureId": "00000000-0000-0000-0000-000000000031",  // string (Guid) или null
    "discountPercent": 10.0,
    ...
  }
]

// СТАЛО
[
  {
    "id": "guid",
    "nomenclatureGroupId": 5,  // number (int) или null
    "nomenclatureId": 31,  // number (int) или null
    "discountPercent": 10.0,
    ...
  }
]
```

**Важно:** `nomenclatureGroupId` также изменен с `Guid` (string) на `int` (number).

## Необходимые изменения во фронтенде

### TypeScript/JavaScript

#### 1. Типы/Интерфейсы

**БЫЛО:**
```typescript
interface OrderItemDto {
  id: string;
  nomenclatureId: string;  // Guid
  nomenclatureName: string;
  // ...
}

interface CartItemDto {
  id: string;
  nomenclatureId: string;  // Guid
  nomenclatureName: string;
  // ...
}

interface DiscountDto {
  id: string;
  nomenclatureGroupId: number | null;  // int
  nomenclatureId: number | null;  // int
  // ...
}
```

**СТАЛО:**
```typescript
interface OrderItemDto {
  id: string;
  nomenclatureId: number;  // int
  nomenclatureName: string;
  // ...
}

interface CartItemDto {
  id: string;
  nomenclatureId: number;  // int
  nomenclatureName: string;
  // ...
}

interface DiscountDto {
  id: string;
  nomenclatureGroupId: number | null;  // int
  nomenclatureId: number | null;  // int
  // ...
}
```

#### 2. Обработка данных

**БЫЛО:**
```typescript
// Парсинг из строки
const nomenclatureId = item.nomenclatureId; // string
const idNumber = parseInt(item.nomenclatureId.split('-')[4], 16); // Конвертация из hex

// Отправка в API
const request = {
  nomenclatureId: "00000000-0000-0000-0000-000000000031"
};
```

**СТАЛО:**
```typescript
// Напрямую число
const nomenclatureId = item.nomenclatureId; // number

// Отправка в API
const request = {
  nomenclatureId: 31  // Просто число
};
```

#### 3. Валидация форм

**БЫЛО:**
```typescript
// Валидация GUID формата
const isValidGuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
```

**СТАЛО:**
```typescript
// Валидация целого числа
const isValidId = Number.isInteger(value) && value > 0;
```

#### 4. Хранение в локальном состоянии/кеше

**БЫЛО:**
```typescript
// Хранение как строка
localStorage.setItem('selectedNomenclatureId', item.nomenclatureId); // string
const id = localStorage.getItem('selectedNomenclatureId'); // string
```

**СТАЛО:**
```typescript
// Хранение как число
localStorage.setItem('selectedNomenclatureId', item.nomenclatureId.toString()); // number -> string
const id = parseInt(localStorage.getItem('selectedNomenclatureId') || '0', 10); // string -> number
```

#### 5. Сравнение значений

**БЫЛО:**
```typescript
// Сравнение строк
if (item.nomenclatureId === selectedId) { ... }
```

**СТАЛО:**
```typescript
// Сравнение чисел
if (item.nomenclatureId === selectedId) { ... } // Работает одинаково, но типы number
```

## Миграция данных

Если во фронтенде хранятся старые значения `nomenclatureId` в формате GUID, необходимо их конвертировать:

```typescript
// Функция конвертации GUID в int (если нужно)
function guidToInt(guid: string): number {
  const parts = guid.split('-');
  if (parts.length === 5 && guid.startsWith('00000000-0000-0000-0000-')) {
    const lastPart = parts[4];
    // Пытаемся распарсить как decimal (убираем ведущие нули)
    const trimmed = lastPart.replace(/^0+/, '') || '0';
    return parseInt(trimmed, 10);
  }
  return 0; // или выбросить ошибку
}

// Использование при миграции
const oldGuid = localStorage.getItem('oldNomenclatureId');
if (oldGuid) {
  const newIntId = guidToInt(oldGuid);
  localStorage.setItem('nomenclatureId', newIntId.toString());
  localStorage.removeItem('oldNomenclatureId');
}
```

**Однако рекомендуется:** Просто очистить старые данные, так как они могут быть в неправильном формате (hex вместо decimal).

## Обратная совместимость

⚠️ **Внимание:** Это breaking change, обратная совместимость отсутствует. Старые версии фронтенда с GUID не будут работать с новым API.

## Пример полной миграции компонента

### React/TypeScript пример

**БЫЛО:**
```typescript
interface OrderItem {
  id: string;
  nomenclatureId: string; // Guid
  // ...
}

const OrderItemComponent: React.FC<{ item: OrderItem }> = ({ item }) => {
  const handleSelect = () => {
    onSelect(item.nomenclatureId); // string
  };
  
  return (
    <div>
      <span>ID: {item.nomenclatureId}</span>
      <button onClick={handleSelect}>Выбрать</button>
    </div>
  );
};
```

**СТАЛО:**
```typescript
interface OrderItem {
  id: string;
  nomenclatureId: number; // int
  // ...
}

const OrderItemComponent: React.FC<{ item: OrderItem }> = ({ item }) => {
  const handleSelect = () => {
    onSelect(item.nomenclatureId); // number
  };
  
  return (
    <div>
      <span>ID: {item.nomenclatureId}</span>
      <button onClick={handleSelect}>Выбрать</button>
    </div>
  );
};
```

## Чеклист для разработчиков фронтенда

- [ ] Обновить TypeScript интерфейсы/типы для всех DTO
- [ ] Найти все места, где используется `nomenclatureId` как строка
- [ ] Обновить валидацию форм (если есть)
- [ ] Проверить localStorage/sessionStorage на наличие старых GUID значений
- [ ] Обновить все API вызовы (отправка и получение)
- [ ] Обновить тесты (unit/integration)
- [ ] Проверить работу фильтрации/поиска по nomenclatureId
- [ ] Обновить документацию фронтенда (если есть)

## Дополнительная информация

### Почему это изменение?

1. **Упрощение:** Нет необходимости конвертировать между GUID и int
2. **Производительность:** Числа занимают меньше места и быстрее сравниваются
3. **Совместимость:** Прямое соответствие с данными из FimBiz
4. **Надежность:** Устранена проблема с неправильной конвертацией hex/decimal

### Что НЕ изменилось?

- Все остальные GUID поля (`id`, `orderId`, `cartId` и т.д.) остались без изменений
- Структура API осталась прежней, изменился только тип полей `nomenclatureId` и `nomenclatureGroupId`

## Контакты

При возникновении вопросов или проблем при миграции обращайтесь к команде бэкенда.
