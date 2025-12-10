# Интеграция счетов на оплату в API заказов

## Обзор

В API заказов добавлена поддержка информации о счетах на оплату. Счета создаются автоматически в системе FimBiz и синхронизируются с интернет-магазином через gRPC.

## Изменения в API

### Обновленная структура OrderDto

В ответах API заказов теперь включено поле `invoice` с информацией о счете на оплату (если счет был создан).

#### Эндпоинты

- `GET /api/orders` - список заказов пользователя
- `GET /api/orders/{id}` - информация о конкретном заказе

Оба эндпоинта возвращают объект `OrderDto` с новым полем `invoice`.

### Структура данных

#### OrderDto

```typescript
interface OrderDto {
  id: string;                    // UUID заказа
  orderNumber: string;           // Номер заказа
  status: OrderStatus;           // Статус заказа
  statusName: string;            // Название статуса
  deliveryType: DeliveryType;    // Тип доставки
  trackingNumber?: string;       // Трек-номер
  totalAmount: number;           // Общая сумма заказа
  createdAt: string;             // Дата создания (ISO 8601)
  items: OrderItemDto[];         // Позиции заказа
  deliveryAddress?: DeliveryAddressDto;
  cargoReceiver?: CargoReceiverDto;
  carrier?: string;              // Название транспортной компании
  attachments: OrderAttachmentDto[];
  invoice?: InvoiceInfoDto;      // ⭐ НОВОЕ: Информация о счете
}
```

#### InvoiceInfoDto

```typescript
interface InvoiceInfoDto {
  id: string;                    // UUID счета в системе интернет-магазина
  invoiceNumber: string;         // Номер счета (например, "123")
  invoiceDate: string;          // Дата создания счета (ISO 8601)
  isConfirmed: boolean;          // Счет подтвержден
  isPaid: boolean;               // Счет оплачен
  pdfUrl?: string;               // ⭐ Относительный URL для скачивания PDF
  fimBizBillId?: number;         // ID счета в системе FimBiz
}
```

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
  if (!order.invoice) {
    return <div>Счет на оплату еще не создан</div>;
  }

  const invoice = order.invoice;

  return (
    <div className="invoice-info">
      <h3>Счет на оплату</h3>
      <p>Номер счета: {invoice.invoiceNumber}</p>
      <p>Дата: {new Date(invoice.invoiceDate).toLocaleDateString()}</p>
      <p>Статус: {invoice.isPaid ? 'Оплачен' : invoice.isConfirmed ? 'Подтвержден' : 'Ожидает оплаты'}</p>
      
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
  return order.invoice !== null && order.invoice !== undefined;
}

function isInvoicePaid(order: OrderDto): boolean {
  return order.invoice?.isPaid ?? false;
}

function isInvoiceConfirmed(order: OrderDto): boolean {
  return order.invoice?.isConfirmed ?? false;
}
```

### Пример 3: Отображение статуса оплаты

```typescript
function InvoiceStatusBadge({ invoice }: { invoice: InvoiceInfoDto }) {
  if (invoice.isPaid) {
    return <span className="badge badge-success">Оплачен</span>;
  }
  
  if (invoice.isConfirmed) {
    return <span className="badge badge-info">Подтвержден</span>;
  }
  
  return <span className="badge badge-warning">Ожидает оплаты</span>;
}
```

## Когда появляется счет

Счет на оплату создается автоматически в следующих случаях:

1. **При создании заказа** со статусом `PROCESSING` - счет создается автоматически в FimBiz
2. **При переходе заказа в статус** `WAITING_FOR_PAYMENT` - если счет еще не создан, он создается автоматически
3. **При создании счета вручную** администратором в системе FimBiz

После создания счета информация о нем приходит через gRPC уведомление и включается в ответ API.

## Статусы счета

- `isPaid = false, isConfirmed = false` - Счет создан, ожидает оплаты
- `isPaid = false, isConfirmed = true` - Счет подтвержден (для постоплаты)
- `isPaid = true` - Счет оплачен

## Важные замечания

1. **Относительный URL:** Поле `pdfUrl` содержит относительный путь к файлу. Фронтенд должен формировать полный URL для скачивания.

2. **Базовый URL FimBiz:** По умолчанию используется `https://api.fimbiz.ru`. Для тестовой среды может быть `https://testapi.fimbiz.ru`.

3. **Авторизация:** При скачивании PDF с сервера FimBiz может потребоваться API ключ в заголовке `x-api-key`. Уточните у бэкенд-команды, требуется ли авторизация для доступа к файлам.

4. **Обработка ошибок:** Всегда обрабатывайте случаи, когда `invoice` может быть `null` или `undefined`, а также когда `pdfUrl` отсутствует.

5. **Кэширование:** PDF файлы могут быть большими. Рекомендуется кэшировать загруженные файлы на клиенте.

## Пример полной интеграции

```typescript
// types.ts
export interface InvoiceInfoDto {
  id: string;
  invoiceNumber: string;
  invoiceDate: string;
  isConfirmed: boolean;
  isPaid: boolean;
  pdfUrl?: string;
  fimBizBillId?: number;
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
      
      {order.invoice && (
        <div className="invoice-section">
          <h3>Счет на оплату</h3>
          <p>Номер: {order.invoice.invoiceNumber}</p>
          <p>Дата: {new Date(order.invoice.invoiceDate).toLocaleDateString()}</p>
          <p>Статус: {order.invoice.isPaid ? 'Оплачен' : 'Ожидает оплаты'}</p>
          
          {order.invoice.pdfUrl && (
            <button onClick={handleDownloadInvoice}>
              Скачать PDF
            </button>
          )}
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

