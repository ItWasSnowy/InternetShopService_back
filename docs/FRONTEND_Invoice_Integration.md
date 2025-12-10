# Интеграция счетов на оплату в API заказов

## Обзор

В API заказов добавлена поддержка информации о счетах на оплату. Счета создаются автоматически в системе FimBiz и синхронизируются с интернет-магазином через gRPC.

## Структура данных заказа (OrderDto)

### Эндпоинты

- `GET /api/orders` - возвращает список всех заказов текущего пользователя (`OrderDto[]`)
- `GET /api/orders/{id}` - возвращает конкретный заказ по ID (`OrderDto`)

### Полная структура OrderDto

```typescript
interface OrderDto {
  // Идентификация
  id: string;                    // UUID заказа
  orderNumber: string;           // Номер заказа (сквозная нумерация, например "ORD-2024-001")
  
  // Статус
  status: OrderStatus;           // Статус заказа (число от 1 до 10)
  statusName: string;            // Название статуса на русском языке (например, "Обрабатывается", "Ожидает оплаты")
  
  // Доставка
  deliveryType: DeliveryType;    // Тип доставки (число: 1, 2 или 3)
  trackingNumber?: string;        // Трек-номер для отслеживания посылки (опционально)
  carrier?: string;              // Название транспортной компании (опционально)
  
  // Финансы
  totalAmount: number;            // Общая сумма заказа (в рублях, decimal)
  
  // Временные метки
  createdAt: string;             // Дата создания заказа (ISO 8601, например "2024-12-10T10:30:00Z")
  
  // Позиции заказа
  items: OrderItemDto[];         // Список позиций заказа (всегда массив, может быть пустым)
  
  // Адрес доставки (опционально)
  deliveryAddress?: DeliveryAddressDto;
  
  // Грузополучатель (опционально)
  cargoReceiver?: CargoReceiverDto;
  
  // Вложения (документы, файлы)
  attachments: OrderAttachmentDto[];  // Список вложений (всегда массив, может быть пустым)
  
  // Счет на оплату (опционально)
  invoice?: InvoiceInfoDto;      // ⭐ Информация о счете (если счет был создан)
}
```

### OrderItemDto (позиция заказа)

```typescript
interface OrderItemDto {
  id: string;                   // UUID позиции заказа
  nomenclatureId: string;        // UUID номенклатуры товара
  nomenclatureName: string;      // Наименование товара
  quantity: number;              // Количество (целое число)
  price: number;                 // Цена за единицу (в рублях, decimal)
  discountPercent: number;       // Процент скидки (decimal, может быть 0)
  totalAmount: number;           // Итоговая сумма позиции с учетом скидки (в рублях, decimal)
}
```

### DeliveryAddressDto (адрес доставки)

```typescript
interface DeliveryAddressDto {
  id: string;                   // UUID адреса
  address: string;              // Полный адрес (обязательное поле)
  city?: string;                // Город (опционально)
  region?: string;              // Регион/область (опционально)
  postalCode?: string;         // Почтовый индекс (опционально)
}
```

### CargoReceiverDto (грузополучатель)

```typescript
interface CargoReceiverDto {
  id: string;                   // UUID грузополучателя
  fullName: string;              // ФИО грузополучателя (обязательное поле)
  passportSeries: string;       // Серия паспорта (обязательное поле)
  passportNumber: string;       // Номер паспорта (обязательное поле)
}
```

### OrderAttachmentDto (вложение/документ)

```typescript
interface OrderAttachmentDto {
  id: string;                   // UUID вложения
  fileName: string;             // Имя файла
  contentType: string;          // MIME-тип файла (например, "application/pdf", "image/jpeg")
  isVisibleToCustomer: boolean; // Видно ли покупателю (true/false)
  createdAt: string;            // Дата создания (ISO 8601)
}
```

### InvoiceInfoDto (информация о счете)

```typescript
interface InvoiceInfoDto {
  pdfUrl?: string;              // Относительный URL для скачивания PDF счета (например, "/Files/OrderFiles/123/bill.pdf")
}
```

**Важно:** Структура упрощена - хранится только относительный URL файла PDF. Все остальные данные о счете (номер, дата, сумма, статус оплаты) находятся в самом PDF файле.

### OrderStatus (статусы заказа)

```typescript
enum OrderStatus {
  Processing = 1,              // Обрабатывается
  AwaitingPayment = 2,         // Ожидает оплаты/Подтверждения счета
  InvoiceConfirmed = 3,        // Счет подтвержден
  Manufacturing = 4,           // Изготавливается
  Assembling = 5,              // Собирается
  TransferredToCarrier = 6,    // Передается в транспортную компанию
  DeliveringByCarrier = 7,     // Доставляется транспортной компанией
  Delivering = 8,              // Доставляется
  AwaitingPickup = 9,          // Ожидает получения
  Received = 10                // Получен
}
```

### DeliveryType (типы доставки)

```typescript
enum DeliveryType {
  Pickup = 1,                  // Самовывоз
  Carrier = 2,                 // Транспортная компания
  SellerDelivery = 3           // Доставка средствами продавца
}
```

### Пример полного ответа API

```json
{
  "id": "123e4567-e89b-12d3-a456-426614174000",
  "orderNumber": "ORD-2024-001",
  "status": 1,
  "statusName": "Обрабатывается",
  "deliveryType": 2,
  "trackingNumber": "TRACK123456",
  "carrier": "Почта России",
  "totalAmount": 15000.50,
  "createdAt": "2024-12-10T10:30:00Z",
  "items": [
    {
      "id": "item-1",
      "nomenclatureId": "nom-123",
      "nomenclatureName": "Товар 1",
      "quantity": 2,
      "price": 5000.00,
      "discountPercent": 10,
      "totalAmount": 9000.00
    },
    {
      "id": "item-2",
      "nomenclatureId": "nom-456",
      "nomenclatureName": "Товар 2",
      "quantity": 1,
      "price": 6000.50,
      "discountPercent": 0,
      "totalAmount": 6000.50
    }
  ],
  "deliveryAddress": {
    "id": "addr-1",
    "address": "ул. Примерная, д. 1, кв. 10",
    "city": "Москва",
    "region": "Московская область",
    "postalCode": "123456"
  },
  "cargoReceiver": {
    "id": "receiver-1",
    "fullName": "Иванов Иван Иванович",
    "passportSeries": "1234",
    "passportNumber": "567890"
  },
  "attachments": [
    {
      "id": "attach-1",
      "fileName": "invoice.pdf",
      "contentType": "application/pdf",
      "isVisibleToCustomer": true,
      "createdAt": "2024-12-10T10:35:00Z"
    }
  ],
  "invoice": {
    "pdfUrl": "/Files/OrderFiles/123/bill.pdf"
  }
}
```

## Изменения в API

### Добавлено поле invoice в OrderDto

В ответах API заказов теперь включено поле `invoice` с информацией о счете на оплату (если счет был создан).

**Полная структура `OrderDto` и всех связанных DTO описана в разделе [Структура данных заказа (OrderDto)](#структура-данных-заказа-orderdto) выше.**

#### Что изменилось

- Добавлено опциональное поле `invoice?: InvoiceInfoDto` в `OrderDto`
- Поле `invoice` содержит только относительный URL PDF файла счета
- Все остальные данные о счете (номер, дата, сумма, статус) находятся в самом PDF файле

## Работа с PDF счетом

### Относительный URL

Поле `pdfUrl` содержит **относительный URL** пути к PDF файлу на сервере FimBiz.

**Важно:** URL может быть:
- Относительным (например: `/Files/OrderFiles/123/bill.pdf`)
- Абсолютным (например: `https://api.fimbiz.ru/Files/OrderFiles/123/bill.pdf`)

### Формирование полного URL для скачивания

Для скачивания PDF файла необходимо сформировать полный URL:

```typescript
function getInvoicePdfUrl(invoice: InvoiceInfoDto): string | null {
  if (!invoice.pdfUrl) {
    return null;
  }

  // Если URL уже абсолютный, возвращаем как есть
  if (invoice.pdfUrl.startsWith('http://') || invoice.pdfUrl.startsWith('https://')) {
    return invoice.pdfUrl;
  }

  // Если относительный, добавляем базовый URL FimBiz
  const fimBizBaseUrl = 'https://api.fimbiz.ru'; // или из конфигурации
  return `${fimBizBaseUrl}${invoice.pdfUrl.startsWith('/') ? '' : '/'}${invoice.pdfUrl}`;
}
```

### Скачивание PDF файла

Для скачивания PDF файла с сервера FimBiz необходимо:

1. **Сформировать полный URL** (см. выше)
2. **Добавить заголовок авторизации** с API ключом FimBiz (если требуется)
3. **Выполнить GET запрос** для получения файла

**Пример на TypeScript/JavaScript:**

```typescript
async function downloadInvoicePdf(invoice: InvoiceInfoDto): Promise<Blob | null> {
  const pdfUrl = getInvoicePdfUrl(invoice);
  if (!pdfUrl) {
    return null;
  }

  try {
    const response = await fetch(pdfUrl, {
      method: 'GET',
      headers: {
        // Если требуется авторизация на FimBiz
        // 'x-api-key': 'your-fimbiz-api-key'
      }
    });

    if (!response.ok) {
      throw new Error(`Failed to download PDF: ${response.statusText}`);
    }

    return await response.blob();
  } catch (error) {
    console.error('Error downloading invoice PDF:', error);
    return null;
  }
}

// Использование
async function handleDownloadInvoice(order: OrderDto) {
  if (!order.invoice) {
    console.warn('Invoice not available');
    return;
  }

  const pdfBlob = await downloadInvoicePdf(order.invoice);
  if (pdfBlob) {
    // Создаем ссылку для скачивания
    const url = window.URL.createObjectURL(pdfBlob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `invoice_${order.invoice.invoiceNumber}.pdf`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    window.URL.revokeObjectURL(url);
  }
}
```

**Пример с использованием axios:**

```typescript
import axios from 'axios';

async function downloadInvoicePdf(invoice: InvoiceInfoDto): Promise<Blob | null> {
  const pdfUrl = getInvoicePdfUrl(invoice);
  if (!pdfUrl) {
    return null;
  }

  try {
    const response = await axios.get(pdfUrl, {
      responseType: 'blob',
      headers: {
        // Если требуется авторизация на FimBiz
        // 'x-api-key': 'your-fimbiz-api-key'
      }
    });

    return response.data;
  } catch (error) {
    console.error('Error downloading invoice PDF:', error);
    return null;
  }
}
```

## Примеры использования

### Пример 1: Отображение информации о счете

```typescript
function OrderInvoiceInfo({ order }: { order: OrderDto }) {
  if (!order.invoice || !order.invoice.pdfUrl) {
    return <div>Счет на оплату еще не создан</div>;
  }

  const invoice = order.invoice;

  return (
    <div className="invoice-info">
      <h3>Счет на оплату</h3>
      
      {invoice.pdfUrl && (
        <button onClick={() => handleDownloadInvoice(order)}>
          Скачать PDF счета
        </button>
      )}
    </div>
  );
}
```

### Пример 2: Проверка наличия счета

```typescript
function hasInvoice(order: OrderDto): boolean {
  return order.invoice !== null && 
         order.invoice !== undefined && 
         !string.IsNullOrEmpty(order.invoice.pdfUrl);
}

function getInvoicePdfUrl(order: OrderDto): string | null {
  if (!order.invoice?.pdfUrl) {
    return null;
  }
  
  const pdfUrl = order.invoice.pdfUrl;
  
  // Если URL уже абсолютный, возвращаем как есть
  if (pdfUrl.startsWith('http://') || pdfUrl.startsWith('https://')) {
    return pdfUrl;
  }
  
  // Если относительный, добавляем базовый URL FimBiz
  const fimBizBaseUrl = 'https://api.fimbiz.ru'; // или из конфигурации
  return `${fimBizBaseUrl}${pdfUrl.startsWith('/') ? '' : '/'}${pdfUrl}`;
}
```

## Когда появляется счет

Счет на оплату создается автоматически в следующих случаях:

1. **При создании заказа** со статусом `PROCESSING` - счет создается автоматически в FimBiz
2. **При переходе заказа в статус** `WAITING_FOR_PAYMENT` - если счет еще не создан, он создается автоматически
3. **При создании счета вручную** администратором в системе FimBiz

После создания счета информация о нем (относительный URL PDF) приходит через gRPC уведомление и включается в ответ API.

**Важно:** Вся информация о счете (номер, дата, статус оплаты и т.д.) находится в самом PDF файле. API возвращает только относительный URL для скачивания этого файла.

## Важные замечания

1. **Относительный URL:** Поле `pdfUrl` содержит относительный путь к файлу (например, `/Files/OrderFiles/123/bill.pdf`). Фронтенд должен формировать полный URL для скачивания.

2. **Базовый URL FimBiz:** По умолчанию используется `https://api.fimbiz.ru`. Для тестовой среды может быть `https://testapi.fimbiz.ru`.

3. **Авторизация:** При скачивании PDF с сервера FimBiz может потребоваться API ключ в заголовке `x-api-key`. Уточните у бэкенд-команды, требуется ли авторизация для доступа к файлам.

4. **Обработка ошибок:** Всегда обрабатывайте случаи, когда `invoice` может быть `null` или `undefined`, а также когда `pdfUrl` отсутствует.

5. **Кэширование:** PDF файлы могут быть большими. Рекомендуется кэшировать загруженные файлы на клиенте.

6. **Минимальная структура:** API возвращает только относительный URL PDF файла. Вся остальная информация о счете (номер, дата, сумма, статус) находится в самом PDF файле.

## Пример полной интеграции

```typescript
// types.ts
export interface InvoiceInfoDto {
  pdfUrl?: string; // Относительный URL (например, "/Files/OrderFiles/123/bill.pdf")
}

export interface OrderDto {
  id: string;
  orderNumber: string;
  status: OrderStatus;
  statusName: string;
  // ... другие поля
  invoice?: InvoiceInfoDto;
}

// invoiceService.ts
export class InvoiceService {
  private readonly fimBizBaseUrl = 'https://api.fimbiz.ru';

  getInvoicePdfUrl(invoice: InvoiceInfoDto): string | null {
    if (!invoice.pdfUrl) {
      return null;
    }

    if (invoice.pdfUrl.startsWith('http://') || invoice.pdfUrl.startsWith('https://')) {
      return invoice.pdfUrl;
    }

    return `${this.fimBizBaseUrl}${invoice.pdfUrl.startsWith('/') ? '' : '/'}${invoice.pdfUrl}`;
  }

  async downloadInvoicePdf(invoice: InvoiceInfoDto): Promise<Blob | null> {
    const pdfUrl = this.getInvoicePdfUrl(invoice);
    if (!pdfUrl) {
      return null;
    }

    try {
      const response = await fetch(pdfUrl, {
        method: 'GET',
        // Добавьте заголовки авторизации, если требуется
      });

      if (!response.ok) {
        throw new Error(`Failed to download PDF: ${response.statusText}`);
      }

      return await response.blob();
    } catch (error) {
      console.error('Error downloading invoice PDF:', error);
      return null;
    }
  }

  async saveInvoicePdf(invoice: InvoiceInfoDto, filename?: string): Promise<void> {
    const pdfBlob = await this.downloadInvoicePdf(invoice);
    if (!pdfBlob) {
      throw new Error('Failed to download PDF');
    }

    const url = window.URL.createObjectURL(pdfBlob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename || `invoice_${invoice.invoiceNumber}.pdf`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    window.URL.revokeObjectURL(url);
  }
}

// OrderComponent.tsx
import React from 'react';
import { OrderDto } from './types';
import { InvoiceService } from './invoiceService';

const invoiceService = new InvoiceService();

export function OrderComponent({ order }: { order: OrderDto }) {
  const handleDownloadInvoice = async () => {
    if (!order.invoice) {
      return;
    }

    try {
      await invoiceService.saveInvoicePdf(order.invoice);
    } catch (error) {
      console.error('Failed to download invoice:', error);
      alert('Не удалось скачать счет');
    }
  };

  return (
    <div>
      <h2>Заказ {order.orderNumber}</h2>
      
      {order.invoice?.pdfUrl && (
        <div className="invoice-section">
          <h3>Счет на оплату</h3>
          
          <button onClick={handleDownloadInvoice}>
            Скачать PDF счета
          </button>
        </div>
      )}
    </div>
  );
}
```

## Changelog

### Версия 1.0 (2024-12-10)
- Добавлено поле `invoice` в `OrderDto`
- Добавлена структура `InvoiceInfoDto` с информацией о счете
- Добавлена поддержка относительных URL для PDF файлов
- Счета автоматически синхронизируются с FimBiz через gRPC

