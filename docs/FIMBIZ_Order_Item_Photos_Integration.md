# Интеграция фотографий товаров заказа с FimBiz

## Обзор

При создании заказа из корзины интернет-магазина, фотографии товаров (URL фотографий) передаются в систему FimBiz через поле `photo_urls` в сообщении `OrderItem` gRPC запроса `CreateOrderRequest`.

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

При отправке заказа в FimBiz через gRPC метод `CreateOrder`, фотографии товаров передаются напрямую в поле `photo_urls` каждой позиции заказа (`OrderItem`).

## Формат данных в OrderItem

Фотографии товаров передаются как массив строк в поле `photo_urls` каждой позиции заказа.

### Структура OrderItem с фотографиями

```protobuf
message OrderItem {
  optional int32 nomenclature_id = 1;
  string name = 2;
  int32 quantity = 3;
  int64 price = 4;
  bool is_available = 5;
  bool requires_manufacturing = 6;
  repeated string photo_urls = 7;  // URL фотографий товара
}
```

### Пример для одной позиции заказа

Если позиция заказа имеет:
- `NomenclatureId`: `123e4567-e89b-12d3-a456-426614174000`
- `UrlPhotos`: `["https://example.com/photo1.jpg", "https://example.com/photo2.jpg"]`

То в gRPC `OrderItem` будет:
```protobuf
OrderItem {
  nomenclature_id: 12345
  name: "Товар"
  quantity: 2
  price: 100000
  is_available: true
  requires_manufacturing: false
  photo_urls: "https://example.com/photo1.jpg"
  photo_urls: "https://example.com/photo2.jpg"
}
```

В C# коде это будет:
```csharp
var grpcItem = new GrpcOrderItem
{
    NomenclatureId = 12345,
    Name = "Товар",
    Quantity = 2,
    Price = 100000,
    IsAvailable = true,
    RequiresManufacturing = false
};
grpcItem.PhotoUrls.AddRange(new[] { 
    "https://example.com/photo1.jpg", 
    "https://example.com/photo2.jpg" 
});
```

## Структура gRPC запроса

```protobuf
message CreateOrderRequest {
  int32 company_id = 1;
  string external_order_id = 2;
  int32 contractor_id = 3;
  string delivery_address = 4;
  DeliveryType delivery_type = 5;
  repeated OrderItem items = 6;  // Каждая позиция содержит photo_urls
  optional string comment = 7;
  optional int32 organization_id = 8;
  map<string, string> metadata = 9;
}

message OrderItem {
  optional int32 nomenclature_id = 1;
  string name = 2;
  int32 quantity = 3;
  int64 price = 4;
  bool is_available = 5;
  bool requires_manufacturing = 6;
  repeated string photo_urls = 7;  // <-- Фотографии передаются здесь
}
```

## Обработка на стороне FimBiz

Фотографии товаров передаются напрямую в поле `photo_urls` каждой позиции заказа, поэтому обработка очень проста:

1. **Извлечь фотографии из OrderItem**:
   - Каждая позиция заказа (`OrderItem`) содержит массив `photo_urls`
   - Фотографии уже доступны как массив строк, не требуется дополнительная десериализация

2. **Сохранить фотографии**:
   - Связать фотографии с соответствующей позицией заказа
   - Фотографии находятся в той же позиции, что и остальные данные товара

### Пример обработки

```python
# Пример на Python
def process_order_items(order_request):
    """
    Обрабатывает позиции заказа и извлекает фотографии.
    """
    for item in order_request.items:
        # Фотографии уже доступны как список строк
        photo_urls = list(item.photo_urls)
        
        # Сохранить фотографии для позиции заказа
        save_item_photos(
            item_id=item.nomenclature_id,
            photo_urls=photo_urls
        )
        
        print(f"Товар {item.name}: {len(photo_urls)} фотографий")
```

```csharp
// Пример на C#
public void ProcessOrderItems(CreateOrderRequest request)
{
    foreach (var item in request.Items)
    {
        // Фотографии уже доступны как коллекция строк
        var photoUrls = item.PhotoUrls.ToList();
        
        // Сохранить фотографии для позиции заказа
        SaveItemPhotos(
            itemId: item.NomenclatureId,
            photoUrls: photoUrls
        );
        
        Console.WriteLine($"Товар {item.Name}: {photoUrls.Count} фотографий");
    }
}
```

## Важные замечания

1. **Формат URL**: Фотографии передаются как URL строки, а не как бинарные данные. FimBiz должен иметь возможность загрузить изображения по этим URL.

2. **Прямая передача**: Фотографии передаются напрямую в поле `photo_urls` позиции заказа. Не требуется JSON сериализация или работа с metadata - это упрощает обработку на стороне FimBiz.

3. **Сопоставление с позициями заказа**: 
   - Фотографии находятся непосредственно в той же позиции заказа (`OrderItem`), что и остальные данные товара
   - Не требуется дополнительное сопоставление по ключам или ID
   - `OrderItem.nomenclature_id` связывает позицию с номенклатурой

4. **Опциональность**: Если у позиции заказа нет фотографий, поле `photo_urls` будет пустым массивом или отсутствовать.

5. **Множественные фотографии**: Одна позиция заказа может иметь несколько фотографий - они передаются как массив строк в поле `photo_urls`.

## Проверка работоспособности

Для проверки корректной передачи фотографий:

1. Создайте тестовый заказ с товарами, имеющими фотографии
2. Проверьте, что в каждой позиции `CreateOrderRequest.items` поле `photo_urls` содержит массив URL строк
3. Убедитесь, что количество фотографий соответствует ожидаемому
4. Проверьте, что все URL доступны для загрузки
5. Убедитесь, что фотографии правильно связаны с соответствующими позициями заказа

### Пример проверки

```csharp
// Проверка в коде
foreach (var item in createOrderRequest.Items)
{
    Console.WriteLine($"Позиция: {item.Name}");
    Console.WriteLine($"Фотографий: {item.PhotoUrls.Count}");
    foreach (var url in item.PhotoUrls)
    {
        Console.WriteLine($"  - {url}");
    }
}
```

## История изменений

- **2024-12-XX**: Изменена передача фотографий товаров с metadata на прямое поле `photo_urls` в `OrderItem`
- **2024-12-XX**: Добавлено поле `repeated string photo_urls = 7` в proto определение `OrderItem`

