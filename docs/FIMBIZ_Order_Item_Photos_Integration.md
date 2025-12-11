# Интеграция фотографий товаров заказа с FimBiz

## Обзор

При создании заказа из корзины интернет-магазина, фотографии товаров (URL фотографий) передаются в систему FimBiz через поле `metadata` в gRPC запросе `CreateOrderRequest`.

## Поток данных

### 1. Добавление товара в корзину

Фронтенд отправляет товар с фотографиями через API:

**POST** `/api/cart/add`

```json
{
  "nomenclatureId": "123e4567-e89b-12d3-a456-426614174000",
  "nomenclatureName": "Товар",
  "quantity": 2,
  "price": 1000.00,
  "urlPhotos": [
    "https://example.com/photos/photo1.jpg",
    "https://example.com/photos/photo2.jpg"
  ]
}
```

Данные сохраняются в таблице `CartItems` в поле `UrlPhotosJson` (JSON массив URL строк).

### 2. Создание заказа из корзины

**POST** `/api/cart/create-order`

Фотографии копируются из корзины в заказ и сохраняются в таблице `OrderItems` в поле `UrlPhotosJson`.

### 3. Отправка заказа в FimBiz

При отправке заказа в FimBiz через gRPC метод `CreateOrder`, фотографии товаров добавляются в поле `metadata` запроса `CreateOrderRequest`.

## Формат данных в metadata

Фотографии товаров передаются в `metadata` со следующими ключами:

- **Ключ**: `item_photos_{NomenclatureId}`
- **Значение**: JSON строка с массивом URL фотографий

Где `{NomenclatureId}` - это UUID номенклатуры из интернет-магазина.

### Пример структуры metadata

```json
{
  "item_photos_123e4567-e89b-12d3-a456-426614174000": "[\"https://example.com/photos/photo1.jpg\",\"https://example.com/photos/photo2.jpg\"]",
  "item_photos_987e6543-e21b-34d5-b654-532125678901": "[\"https://example.com/photos/item2_photo1.jpg\"]"
}
```

### Пример для одной позиции заказа

Если позиция заказа имеет:
- `NomenclatureId`: `123e4567-e89b-12d3-a456-426614174000`
- `UrlPhotos`: `["https://example.com/photo1.jpg", "https://example.com/photo2.jpg"]`

То в `metadata` будет добавлено:
```json
{
  "item_photos_123e4567-e89b-12d3-a456-426614174000": "[\"https://example.com/photo1.jpg\",\"https://example.com/photo2.jpg\"]"
}
```

## Структура gRPC запроса

```protobuf
message CreateOrderRequest {
  int32 company_id = 1;
  string external_order_id = 2;
  int32 contractor_id = 3;
  string delivery_address = 4;
  DeliveryType delivery_type = 5;
  repeated OrderItem items = 6;
  optional string comment = 7;
  optional int32 organization_id = 8;
  map<string, string> metadata = 9;  // <-- Фотографии передаются здесь
}
```

## Обработка на стороне FimBiz

Для обработки фотографий товаров в FimBiz необходимо:

1. **Извлечь фотографии из metadata**:
   - Искать ключи, начинающиеся с префикса `item_photos_`
   - Извлечь `NomenclatureId` из ключа (часть после префикса)
   - Десериализовать значение как JSON массив строк

2. **Связать фотографии с позициями заказа**:
   - Сопоставить `NomenclatureId` из ключа metadata с позициями заказа
   - Сохранить фотографии в соответствующие позиции заказа

### Пример обработки (псевдокод)

```python
# Пример на Python
import json

def extract_item_photos_from_metadata(metadata: dict) -> dict:
    """
    Извлекает фотографии товаров из metadata.
    
    Возвращает словарь: {nomenclature_id: [list of photo URLs]}
    """
    item_photos = {}
    prefix = "item_photos_"
    
    for key, value in metadata.items():
        if key.startswith(prefix):
            nomenclature_id = key[len(prefix):]
            try:
                photo_urls = json.loads(value)
                item_photos[nomenclature_id] = photo_urls
            except json.JSONDecodeError:
                # Обработка ошибки парсинга JSON
                continue
    
    return item_photos

# Использование
metadata = {
    "item_photos_123e4567-e89b-12d3-a456-426614174000": '["https://example.com/photo1.jpg","https://example.com/photo2.jpg"]',
    "item_photos_987e6543-e21b-34d5-b654-532125678901": '["https://example.com/item2.jpg"]'
}

photos = extract_item_photos_from_metadata(metadata)
# Результат:
# {
#   "123e4567-e89b-12d3-a456-426614174000": ["https://example.com/photo1.jpg", "https://example.com/photo2.jpg"],
#   "987e6543-e21b-34d5-b654-532125678901": ["https://example.com/item2.jpg"]
# }
```

```csharp
// Пример на C#
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;

public Dictionary<string, List<string>> ExtractItemPhotosFromMetadata(
    IDictionary<string, string> metadata)
{
    var itemPhotos = new Dictionary<string, List<string>>();
    const string prefix = "item_photos_";
    
    foreach (var kvp in metadata)
    {
        if (kvp.Key.StartsWith(prefix))
        {
            var nomenclatureId = kvp.Key.Substring(prefix.Length);
            try
            {
                var photoUrls = JsonSerializer.Deserialize<List<string>>(kvp.Value);
                if (photoUrls != null && photoUrls.Any())
                {
                    itemPhotos[nomenclatureId] = photoUrls;
                }
            }
            catch (JsonException)
            {
                // Обработка ошибки парсинга JSON
                continue;
            }
        }
    }
    
    return itemPhotos;
}
```

## Важные замечания

1. **Формат URL**: Фотографии передаются как URL строки, а не как бинарные данные. FimBiz должен иметь возможность загрузить изображения по этим URL.

2. **Кодирование JSON**: Значения в metadata хранятся как JSON строки (двойное кодирование):
   - Сначала список URL сериализуется в JSON: `["url1", "url2"]`
   - Затем эта строка передается как значение в metadata (строковый тип)

3. **Сопоставление с позициями заказа**: 
   - `NomenclatureId` в ключе metadata - это UUID из интернет-магазина
   - В gRPC запросе `OrderItem.nomenclature_id` - это преобразованный `int32` (первые 4 байта UUID)
   - Для сопоставления может потребоваться дополнительная логика или таблица соответствия

4. **Опциональность**: Если у позиции заказа нет фотографий, соответствующая запись в metadata не добавляется.

5. **Множественные фотографии**: Одна позиция заказа может иметь несколько фотографий (массив URL).

## Проверка работоспособности

Для проверки корректной передачи фотографий:

1. Создайте тестовый заказ с товарами, имеющими фотографии
2. Проверьте, что в `CreateOrderRequest.metadata` присутствуют ключи вида `item_photos_{UUID}`
3. Проверьте, что значения являются валидными JSON массивами URL строк
4. Убедитесь, что все URL доступны для загрузки

## История изменений

- **2024-12-XX**: Добавлена передача фотографий товаров через metadata в CreateOrderRequest

