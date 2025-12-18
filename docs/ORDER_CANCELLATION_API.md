# API документация: Отмена заказов

## Обзор

Функция отмены заказов позволяет пользователям отменять свои заказы на определенных этапах обработки. Отмена возможна только для заказов со статусами **"Обрабатывается"** или **"Ожидает оплаты"**.

## Новый статус заказа

### `Cancelled` (11) - Отменен

**Описание:** Финальный статус заказа, означающий его отмену.

**Особенности:**
- Заказ не может быть изменен после установки этого статуса
- При отмене заказа отправляется уведомление на email контрагента
- Информация об отмене сохраняется в истории статусов заказа
- Отмена синхронизируется с системой FimBiz

## Полный список статусов заказа

| Значение | Название | Описание | Возможность отмены |
|----------|----------|----------|-------------------|
| `Processing` (1) | Обрабатывается | Заказ принят и обрабатывается | ✅ Да |
| `AwaitingPayment` (2) | Ожидает оплаты | Ожидает подтверждения оплаты | ✅ Да |
| `InvoiceConfirmed` (3) | Счет подтвержден | Счет подтвержден, начинается выполнение | ❌ Нет |
| `Manufacturing` (4) | Изготавливается | Товары изготавливаются | ❌ Нет |
| `Assembling` (5) | Собирается | Заказ собирается на складе | ❌ Нет |
| `TransferredToCarrier` (6) | Передан в ТК | Передан в транспортную компанию | ❌ Нет |
| `DeliveringByCarrier` (7) | Доставляется ТК | Доставляется транспортной компанией | ❌ Нет |
| `Delivering` (8) | Доставляется | Находится в процессе доставки | ❌ Нет |
| `AwaitingPickup` (9) | Ожидает получения | Готов к получению | ❌ Нет |
| `Received` (10) | Получен | Заказ получен клиентом | ❌ Нет |
| `Cancelled` (11) | Отменен | Заказ отменен | - |

## API Endpoint: Отмена заказа

### `POST /api/orders/{orderId}/cancel`

Отменяет заказ пользователя.

#### Авторизация

Требуется JWT токен в заголовке `Authorization: Bearer {token}`

#### Параметры пути

| Параметр | Тип | Описание |
|----------|-----|----------|
| `orderId` | `Guid` | ID заказа для отмены |

#### Тело запроса (опционально)

```json
{
  "reason": "string"  // Причина отмены (опционально)
}
```

**Параметры:**
- `reason` (string, опционально) - Причина отмены заказа. Будет сохранена в истории статусов и отправлена в уведомлении.

#### Успешный ответ

**HTTP Status:** `200 OK`

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "orderNumber": "ORD-2024-001234",
  "status": "Cancelled",
  "statusName": "Отменен",
  "deliveryType": "Pickup",
  "trackingNumber": null,
  "carrier": null,
  "totalAmount": 15000.00,
  "createdAt": "2024-12-17T10:30:00Z",
  "items": [
    {
      "id": "1234-5678-90ab-cdef",
      "nomenclatureId": "9876-5432-10ab-cdef",
      "nomenclatureName": "Товар 1",
      "quantity": 2,
      "price": 5000.00,
      "discountPercent": 10.0,
      "totalAmount": 9000.00,
      "urlPhotos": [
        "https://example.com/photo1.jpg"
      ]
    }
  ],
  "deliveryAddress": {
    "id": "abcd-1234-efgh-5678",
    "address": "ул. Примерная, д. 1",
    "city": "Москва",
    "region": "Московская область",
    "postalCode": "123456"
  },
  "cargoReceiver": null,
  "invoice": null,
  "attachments": [],
  "statusHistory": [
    {
      "status": "Processing",
      "statusName": "Обрабатывается",
      "changedAt": "2024-12-17T10:30:00Z",
      "comment": null
    },
    {
      "status": "Cancelled",
      "statusName": "Отменен",
      "changedAt": "2024-12-17T12:45:00Z",
      "comment": "Отменен пользователем. Причина: Передумал"
    }
  ]
}
```

#### Ошибки

##### 400 Bad Request - Отмена невозможна

```json
{
  "error": "Отмена заказа возможна только со статусов 'Обрабатывается' или 'Ожидает оплаты'"
}
```

**Причины:**
- Заказ находится в статусе, из которого отмена невозможна
- Заказ уже отменен

##### 401 Unauthorized

```json
{
  "error": "Пользователь не авторизован"
}
```

**Причина:** Отсутствует или недействителен JWT токен

##### 403 Forbidden

```json
{
  "error": "Заказ не принадлежит текущему пользователю"
}
```

**Причина:** Попытка отменить чужой заказ

##### 404 Not Found

```json
{
  "error": "Заказ не найден"
}
```

**Причина:** Заказ с указанным ID не существует

##### 500 Internal Server Error

```json
{
  "error": "Внутренняя ошибка сервера"
}
```

**Причина:** Непредвиденная ошибка на сервере

## Примеры использования

### Пример 1: Отмена заказа без причины

**Запрос:**
```http
POST /api/orders/3fa85f64-5717-4562-b3fc-2c963f66afa6/cancel HTTP/1.1
Host: api.example.com
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
Content-Type: application/json

{}
```

**Ответ:**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "orderNumber": "ORD-2024-001234",
  "status": "Cancelled",
  "statusName": "Отменен",
  "totalAmount": 15000.00,
  "statusHistory": [
    {
      "status": "Cancelled",
      "statusName": "Отменен",
      "changedAt": "2024-12-17T12:45:00Z",
      "comment": "Отменен пользователем"
    }
  ]
}
```

### Пример 2: Отмена заказа с указанием причины

**Запрос:**
```http
POST /api/orders/3fa85f64-5717-4562-b3fc-2c963f66afa6/cancel HTTP/1.1
Host: api.example.com
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
Content-Type: application/json

{
  "reason": "Нашел дешевле у конкурентов"
}
```

**Ответ:**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "orderNumber": "ORD-2024-001234",
  "status": "Cancelled",
  "statusName": "Отменен",
  "totalAmount": 15000.00,
  "statusHistory": [
    {
      "status": "Cancelled",
      "statusName": "Отменен",
      "changedAt": "2024-12-17T12:45:00Z",
      "comment": "Отменен пользователем. Причина: Нашел дешевле у конкурентов"
    }
  ]
}
```

### Пример 3: Попытка отменить заказ в неподходящем статусе

**Запрос:**
```http
POST /api/orders/3fa85f64-5717-4562-b3fc-2c963f66afa6/cancel HTTP/1.1
Host: api.example.com
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
Content-Type: application/json

{
  "reason": "Передумал"
}
```

**Ответ (если заказ в статусе "Изготавливается"):**
```http
HTTP/1.1 400 Bad Request
Content-Type: application/json

{
  "error": "Отмена заказа возможна только со статусов 'Обрабатывается' или 'Ожидает оплаты'"
}
```

## Диаграмма статусов заказа

```
┌──────────────┐
│  Processing  │ ◄─── Создание заказа
│      (1)     │
└──────┬───────┘
       │
       │ (Отмена возможна ✅)
       ▼
┌──────────────────┐
│ AwaitingPayment  │
│       (2)        │
└──────┬───────────┘
       │
       │ (Отмена возможна ✅)
       ▼
┌──────────────────┐
│ InvoiceConfirmed │
│       (3)        │
└──────┬───────────┘
       │
       │ (Отмена НЕВОЗМОЖНА ❌)
       ▼
      ...
       │
       ▼
┌──────────────┐
│   Received   │
│     (10)     │
└──────────────┘

       │
       │ (В любой момент из статусов 1-2)
       ▼
┌──────────────┐
│  Cancelled   │ ◄─── Финальный статус
│     (11)     │
└──────────────┘
```

## Уведомления

При отмене заказа:

1. **Email уведомление** отправляется на адрес контрагента с информацией:
   - Номер заказа
   - Причина отмены (если указана)
   - Дата и время отмены

2. **Синхронизация с FimBiz:**
   - Статус отмены передается в систему FimBiz через gRPC
   - FimBiz получает информацию об отмене в реальном времени

## Рекомендации для фронтенда

### 1. Отображение кнопки отмены

Кнопка "Отменить заказ" должна отображаться только если:
```javascript
const canCancelOrder = (order) => {
  return order.status === 'Processing' || order.status === 'AwaitingPayment';
};
```

### 2. Подтверждение отмены

Рекомендуется показывать модальное окно с подтверждением:
```javascript
const confirmCancellation = async (orderId, reason) => {
  const confirmed = await showConfirmDialog({
    title: 'Отменить заказ?',
    message: 'Вы уверены, что хотите отменить этот заказ?',
    confirmText: 'Да, отменить',
    cancelText: 'Нет, оставить'
  });
  
  if (confirmed) {
    await cancelOrder(orderId, reason);
  }
};
```

### 3. Обработка ошибок

```javascript
const cancelOrder = async (orderId, reason) => {
  try {
    const response = await fetch(`/api/orders/${orderId}/cancel`, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${token}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({ reason })
    });
    
    if (!response.ok) {
      const error = await response.json();
      
      if (response.status === 400) {
        showError('Отмена невозможна', error.error);
      } else if (response.status === 403) {
        showError('Ошибка доступа', 'У вас нет прав на отмену этого заказа');
      } else {
        showError('Ошибка', 'Не удалось отменить заказ');
      }
      
      return;
    }
    
    const updatedOrder = await response.json();
    showSuccess('Заказ успешно отменен');
    updateOrderInList(updatedOrder);
    
  } catch (error) {
    showError('Ошибка сети', 'Проверьте подключение к интернету');
  }
};
```

### 4. Отображение статуса "Отменен"

```javascript
const getStatusBadgeClass = (status) => {
  const statusClasses = {
    'Processing': 'badge-info',
    'AwaitingPayment': 'badge-warning',
    'InvoiceConfirmed': 'badge-success',
    'Manufacturing': 'badge-primary',
    'Assembling': 'badge-primary',
    'TransferredToCarrier': 'badge-primary',
    'DeliveringByCarrier': 'badge-primary',
    'Delivering': 'badge-primary',
    'AwaitingPickup': 'badge-info',
    'Received': 'badge-success',
    'Cancelled': 'badge-danger' // ← Красный badge для отмененных
  };
  
  return statusClasses[status] || 'badge-secondary';
};
```

### 5. Пример React компонента

```jsx
import React, { useState } from 'react';

const OrderCancellationButton = ({ order, onOrderUpdated }) => {
  const [isLoading, setIsLoading] = useState(false);
  const [showReasonModal, setShowReasonModal] = useState(false);
  const [reason, setReason] = useState('');
  
  const canCancel = order.status === 'Processing' || 
                    order.status === 'AwaitingPayment';
  
  if (!canCancel) {
    return null;
  }
  
  const handleCancel = async () => {
    setIsLoading(true);
    
    try {
      const response = await fetch(`/api/orders/${order.id}/cancel`, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${localStorage.getItem('token')}`,
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({ reason: reason || undefined })
      });
      
      if (!response.ok) {
        const error = await response.json();
        alert(error.error);
        return;
      }
      
      const updatedOrder = await response.json();
      onOrderUpdated(updatedOrder);
      setShowReasonModal(false);
      alert('Заказ успешно отменен');
      
    } catch (error) {
      alert('Ошибка при отмене заказа');
    } finally {
      setIsLoading(false);
    }
  };
  
  return (
    <>
      <button 
        className="btn btn-danger"
        onClick={() => setShowReasonModal(true)}
        disabled={isLoading}
      >
        Отменить заказ
      </button>
      
      {showReasonModal && (
        <div className="modal">
          <h3>Отмена заказа</h3>
          <textarea
            placeholder="Укажите причину отмены (необязательно)"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
          />
          <button onClick={handleCancel} disabled={isLoading}>
            {isLoading ? 'Отменяем...' : 'Подтвердить отмену'}
          </button>
          <button onClick={() => setShowReasonModal(false)}>
            Закрыть
          </button>
        </div>
      )}
    </>
  );
};

export default OrderCancellationButton;
```

## Изменения в существующих endpoint'ах

### GET /api/orders

Теперь возвращает заказы со статусом `Cancelled` в списке.

### GET /api/orders/{id}

В `statusHistory` появляется запись о отмене с комментарием, содержащим причину.

## История изменений

| Дата | Версия | Изменения |
|------|--------|-----------|
| 2024-12-17 | 1.0 | Добавлен статус `Cancelled` и endpoint отмены заказов |

## Поддержка

При возникновении вопросов или проблем обращайтесь к backend команде.

