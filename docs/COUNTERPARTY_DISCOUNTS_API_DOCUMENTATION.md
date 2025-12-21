# API Документация: Получение скидок контрагента

Данная документация описывает API для получения списка скидок текущего контрагента в интернет-магазине.

## Базовый URL

```
https://test.fimbiz.ru/api/counterparty
```

Для локальной разработки:
```
http://localhost:5133/api/counterparty
```

## Аутентификация

Все запросы к API требуют JWT токен в заголовке `Authorization`:

```
Authorization: Bearer <your_access_token>
```

Токен получается после авторизации через `/api/auth/verify-code` или `/api/auth/login`.

---

## Endpoint: Получить скидки контрагента

Получает список всех активных скидок для текущего авторизованного контрагента. Скидки синхронизируются автоматически из FimBiz и могут применяться как к конкретным товарам, так и к группам номенклатуры.

**Endpoint:** `GET /api/counterparty/discounts`

**Заголовки:**
```
Authorization: Bearer <access_token>
Content-Type: application/json
```

**Параметры запроса:** отсутствуют

**Ответ (200 OK):**

Массив объектов `DiscountDto`:

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "nomenclatureGroupId": "550e8400-e29b-41d4-a716-446655440000",
    "nomenclatureId": null,
    "discountPercent": 15.0,
    "validFrom": "2024-01-01T00:00:00Z",
    "validTo": "2024-12-31T23:59:59Z",
    "isActive": true
  },
  {
    "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    "nomenclatureGroupId": null,
    "nomenclatureId": "123e4567-e89b-12d3-a456-426614174000",
    "discountPercent": 20.0,
    "validFrom": "2024-01-01T00:00:00Z",
    "validTo": null,
    "isActive": true
  }
]
```

**Структура объекта DiscountDto:**

| Поле | Тип | Описание |
|------|-----|----------|
| `id` | `Guid` | Уникальный идентификатор скидки |
| `nomenclatureGroupId` | `int?` | ID группы номенклатуры (null, если скидка на конкретный товар) |
| `nomenclatureId` | `int?` | ID конкретного товара (null, если скидка на группу) |
| `discountPercent` | `decimal` | Процент скидки (от 0 до 100) |
| `validFrom` | `DateTime` | Дата начала действия скидки (UTC) |
| `validTo` | `DateTime?` | Дата окончания действия скидки (UTC, null = без ограничений) |
| `isActive` | `bool` | Активна ли скидка |

**Типы скидок:**

1. **Скидка на группу номенклатуры:**
   - `nomenclatureGroupId` заполнен
   - `nomenclatureId` = `null`
   - Применяется ко всем товарам в группе

2. **Скидка на конкретный товар:**
   - `nomenclatureId` заполнен
   - `nomenclatureGroupId` = `null`
   - Применяется только к указанному товару

**Важно:** В одной скидке может быть указана либо группа, либо конкретный товар, но не оба одновременно.

**Ошибки:**

- `401 Unauthorized` - пользователь не авторизован или токен недействителен
  ```json
  {
    "error": "Пользователь не авторизован"
  }
  ```

- `404 Not Found` - контрагент не найден
  ```json
  {
    "error": "Контрагент не найден"
  }
  ```

- `500 Internal Server Error` - внутренняя ошибка сервера
  ```json
  {
    "error": "Внутренняя ошибка сервера"
  }
  ```

---

## Примеры использования

### JavaScript/TypeScript (Fetch API)

```typescript
async function getCounterpartyDiscounts(accessToken: string): Promise<DiscountDto[]> {
  const response = await fetch('https://test.fimbiz.ru/api/counterparty/discounts', {
    method: 'GET',
    headers: {
      'Authorization': `Bearer ${accessToken}`,
      'Content-Type': 'application/json'
    }
  });

  if (!response.ok) {
    if (response.status === 401) {
      throw new Error('Не авторизован');
    }
    if (response.status === 404) {
      throw new Error('Контрагент не найден');
    }
    throw new Error('Ошибка при получении скидок');
  }

  const discounts: DiscountDto[] = await response.json();
  return discounts;
}

// Использование
try {
  const discounts = await getCounterpartyDiscounts(accessToken);
  console.log(`Получено ${discounts.length} скидок`);
  
  discounts.forEach(discount => {
    if (discount.nomenclatureId) {
      console.log(`Скидка ${discount.discountPercent}% на товар ${discount.nomenclatureId}`);
    } else if (discount.nomenclatureGroupId) {
      console.log(`Скидка ${discount.discountPercent}% на группу ${discount.nomenclatureGroupId}`);
    }
  });
} catch (error) {
  console.error('Ошибка:', error);
}
```

### JavaScript/TypeScript (Axios)

```typescript
import axios from 'axios';

async function getCounterpartyDiscounts(accessToken: string): Promise<DiscountDto[]> {
  try {
    const response = await axios.get<DiscountDto[]>(
      'https://test.fimbiz.ru/api/counterparty/discounts',
      {
        headers: {
          'Authorization': `Bearer ${accessToken}`,
          'Content-Type': 'application/json'
        }
      }
    );
    
    return response.data;
  } catch (error) {
    if (axios.isAxiosError(error)) {
      if (error.response?.status === 401) {
        throw new Error('Не авторизован');
      }
      if (error.response?.status === 404) {
        throw new Error('Контрагент не найден');
      }
    }
    throw error;
  }
}
```

### C# (.NET)

```csharp
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

public class DiscountDto
{
    public Guid Id { get; set; }
    public int? NomenclatureGroupId { get; set; }
    public int? NomenclatureId { get; set; }
    public decimal DiscountPercent { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public bool IsActive { get; set; }
}

public async Task<List<DiscountDto>> GetCounterpartyDiscountsAsync(string accessToken)
{
    using var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Authorization = 
        new AuthenticationHeaderValue("Bearer", accessToken);
    
    var response = await httpClient.GetAsync(
        "https://test.fimbiz.ru/api/counterparty/discounts");
    
    response.EnsureSuccessStatusCode();
    
    var json = await response.Content.ReadAsStringAsync();
    var discounts = JsonSerializer.Deserialize<List<DiscountDto>>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    });
    
    return discounts ?? new List<DiscountDto>();
}
```

### Python

```python
import requests
from typing import List, Optional
from datetime import datetime
from uuid import UUID

class DiscountDto:
    def __init__(self, data: dict):
        self.id = UUID(data['id'])
        self.nomenclature_group_id = UUID(data['nomenclatureGroupId']) if data.get('nomenclatureGroupId') else None
        self.nomenclature_id = UUID(data['nomenclatureId']) if data.get('nomenclatureId') else None
        self.discount_percent = float(data['discountPercent'])
        self.valid_from = datetime.fromisoformat(data['validFrom'].replace('Z', '+00:00'))
        self.valid_to = datetime.fromisoformat(data['validTo'].replace('Z', '+00:00')) if data.get('validTo') else None
        self.is_active = bool(data['isActive'])

def get_counterparty_discounts(access_token: str) -> List[DiscountDto]:
    url = 'https://test.fimbiz.ru/api/counterparty/discounts'
    headers = {
        'Authorization': f'Bearer {access_token}',
        'Content-Type': 'application/json'
    }
    
    response = requests.get(url, headers=headers)
    response.raise_for_status()
    
    discounts_data = response.json()
    return [DiscountDto(d) for d in discounts_data]

# Использование
try:
    discounts = get_counterparty_discounts(access_token)
    print(f'Получено {len(discounts)} скидок')
    
    for discount in discounts:
        if discount.nomenclature_id:
            print(f'Скидка {discount.discount_percent}% на товар {discount.nomenclature_id}')
        elif discount.nomenclature_group_id:
            print(f'Скидка {discount.discount_percent}% на группу {discount.nomenclature_group_id}')
except requests.exceptions.HTTPError as e:
    if e.response.status_code == 401:
        print('Не авторизован')
    elif e.response.status_code == 404:
        print('Контрагент не найден')
    else:
        print(f'Ошибка: {e}')
```

---

## Применение скидок

### Приоритет применения скидок

При расчете цены товара скидки применяются в следующем порядке приоритета:

1. **Скидка на конкретный товар** (наивысший приоритет)
   - Если у контрагента есть скидка с `nomenclatureId`, соответствующая ID товара
   - Применяется независимо от наличия скидок на группы

2. **Скидка на группу номенклатуры**
   - Если у контрагента есть скидка с `nomenclatureGroupId`, соответствующая группе товара
   - Применяется только если нет скидки на конкретный товар

### Проверка действительности скидки

Скидка считается действительной, если выполняются все условия:

1. ✅ `isActive = true` — скидка активна
2. ✅ Текущая дата в пределах действия:
   - `validFrom <= текущая_дата`
   - `validTo == null` ИЛИ `validTo >= текущая_дата`
3. ✅ Контрагент существует и не удален

### Пример расчета цены со скидкой

```typescript
function calculatePriceWithDiscount(
  basePrice: number, 
  discounts: DiscountDto[], 
  nomenclatureId: string,
  nomenclatureGroupId?: string
): { finalPrice: number; discountPercent: number } {
  // 1. Ищем скидку на конкретный товар
  const itemDiscount = discounts.find(d => 
    d.nomenclatureId === nomenclatureId && 
    d.isActive &&
    isDiscountValid(d)
  );
  
  if (itemDiscount) {
    const discountAmount = basePrice * (itemDiscount.discountPercent / 100);
    return {
      finalPrice: basePrice - discountAmount,
      discountPercent: itemDiscount.discountPercent
    };
  }
  
  // 2. Ищем скидку на группу
  if (nomenclatureGroupId) {
    const groupDiscount = discounts.find(d => 
      d.nomenclatureGroupId === nomenclatureGroupId &&
      d.isActive &&
      isDiscountValid(d)
    );
    
    if (groupDiscount) {
      const discountAmount = basePrice * (groupDiscount.discountPercent / 100);
      return {
        finalPrice: basePrice - discountAmount,
        discountPercent: groupDiscount.discountPercent
      };
    }
  }
  
  // Нет применимой скидки
  return {
    finalPrice: basePrice,
    discountPercent: 0
  };
}

function isDiscountValid(discount: DiscountDto): boolean {
  const now = new Date();
  const validFrom = new Date(discount.validFrom);
  const validTo = discount.validTo ? new Date(discount.validTo) : null;
  
  return validFrom <= now && (!validTo || validTo >= now);
}
```

---

## Синхронизация скидок

Скидки автоматически синхронизируются из FimBiz через gRPC:

1. **Автоматическая синхронизация:**
   - При получении изменений контрагента через подписку на изменения
   - При полной синхронизации контрагентов
   - Скидки обновляются в реальном времени

2. **Ручная синхронизация:**
   - Можно вызвать `POST /api/counterparty/sync` для принудительной синхронизации данных контрагента, включая скидки

3. **Фильтрация:**
   - API возвращает только активные скидки (`isActive = true`)
   - Скидки с истекшим сроком действия автоматически исключаются
   - Скидки, которые еще не начали действовать, также исключаются

---

## Примеры ответов

### Пример 1: Скидка на группу номенклатуры

```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "nomenclatureGroupId": "550e8400-e29b-41d4-a716-446655440000",
  "nomenclatureId": null,
  "discountPercent": 10.0,
  "validFrom": "2024-01-01T00:00:00Z",
  "validTo": "2024-12-31T23:59:59Z",
  "isActive": true
}
```

**Применение:** Скидка 10% применяется ко всем товарам в группе с ID `550e8400-e29b-41d4-a716-446655440000`.

### Пример 2: Скидка на конкретный товар

```json
{
  "id": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
  "nomenclatureGroupId": null,
  "nomenclatureId": "123e4567-e89b-12d3-a456-426614174000",
  "discountPercent": 20.0,
  "validFrom": "2024-01-01T00:00:00Z",
  "validTo": null,
  "isActive": true
}
```

**Применение:** Скидка 20% применяется только к товару с ID `123e4567-e89b-12d3-a456-426614174000`. Срок действия не ограничен (`validTo = null`).

### Пример 3: Пустой список скидок

```json
[]
```

**Применение:** У контрагента нет активных скидок на данный момент.

---

## Примечания

1. **Кэширование:** Рекомендуется кэшировать скидки на клиенте для уменьшения количества запросов к API. Обновляйте кэш при изменении корзины или периодически (например, каждые 5 минут).

2. **Производительность:** Список скидок обычно небольшой (до 100 элементов), поэтому можно загружать все скидки сразу при загрузке страницы каталога.

3. **Обратная совместимость:** Поля `nomenclatureId` и `nomenclatureGroupId` являются опциональными. Старые версии клиентов, которые не знают о скидках на конкретные товары, продолжат работать со скидками на группы.

4. **Валидация:** Всегда проверяйте `isActive`, `validFrom` и `validTo` на клиенте перед применением скидки, даже если сервер уже фильтрует неактивные скидки.

---

## Связанная документация

- [API Документация: Работа с корзиной](./CART_API_DOCUMENTATION.md) - применение скидок в корзине
- [gRPC Интеграция скидок контрагентов](../TimeTracingServer/docs/grpc/contractor-discounts-integration.md) - описание получения скидок из FimBiz
- [API Документация: Контрагент](./API_DOCUMENTATION.md) - общая документация по API контрагента


