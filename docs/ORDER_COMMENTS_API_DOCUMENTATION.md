# API документация: Комментарии к заказам

**Версия:** 1.1  
**Дата обновления:** 2024-12-17

## Обзор

Система комментариев к заказам позволяет клиентам и сотрудникам добавлять комментарии к заказам. Комментарии синхронизируются с системой FimBiz через gRPC.

**Базовый URL:** `/api/orders/{orderId}/comments`

**Авторизация:** Все запросы требуют JWT токен в заголовке `Authorization: Bearer {token}`

## Endpoints

### 1. Получить все комментарии к заказу

**GET** `/api/orders/{orderId}/comments`

Получает список всех комментариев для указанного заказа.

**Параметры пути:**
- `orderId` (Guid, обязательный) - ID заказа

**Ответ:**

Успешный ответ (200 OK):
```json
[
  {
    "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "orderId": "f1e2d3c4-b5a6-7890-1234-567890abcdef",
    "externalCommentId": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
    "commentText": "Спасибо за быструю обработку заказа!",
    "authorProfileId": null,
    "authorUserId": "f1e2d3c4-b5a6-7890-1234-567890abcdef",
    "authorName": "Иван Иванов",
    "isFromInternetShop": true,
    "createdAt": "2024-12-16T10:30:00Z",
    "attachments": [
      {
        "id": "c3d4e5f6-a7b8-9012-cdef-123456789012",
        "fileName": "photo.jpg",
        "contentType": "image/jpeg",
        "fileUrl": "https://example.com/files/comments/photo.jpg",
        "createdAt": "2024-12-16T10:30:00Z"
      }
    ]
  },
  {
    "id": "d4e5f6a7-b8c9-0123-def4-567890abcdef",
    "orderId": "f1e2d3c4-b5a6-7890-1234-567890abcdef",
    "externalCommentId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "commentText": "Товар будет готов к выдаче завтра",
    "authorProfileId": 200,
    "authorUserId": null,
    "authorName": null,
    "isFromInternetShop": false,
    "createdAt": "2024-12-16T09:15:00Z",
    "attachments": []
  }
]
```

**Ошибки:**
- `401 Unauthorized` - Пользователь не авторизован
- `500 Internal Server Error` - Внутренняя ошибка сервера

**Пример запроса:**
```bash
curl -X GET "https://api.example.com/api/orders/f1e2d3c4-b5a6-7890-1234-567890abcdef/comments" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

---

### 2. Создать комментарий к заказу

**POST** `/api/orders/{orderId}/comments`

Создает новый комментарий к заказу. Комментарий автоматически отправляется в FimBiz, если заказ синхронизирован с FimBiz.

**Параметры пути:**
- `orderId` (Guid, обязательный) - ID заказа

**Тело запроса:**
```json
{
  "commentText": "Спасибо за быструю обработку заказа!",
  "authorName": "Иван Иванов",
  "attachments": [
    {
      "fileName": "photo.jpg",
      "contentType": "image/jpeg",
      "fileUrl": "https://example.com/files/comments/photo.jpg"
    }
  ]
}
```

**Поля:**
- `commentText` (string, обязательный) - Текст комментария (максимум 5000 символов)
- `authorName` (string, опциональный) - Имя автора комментария (для комментариев из интернет-магазина)
- `attachments` (array, опциональный) - Массив прикрепленных файлов

**Поля вложенного объекта `attachments`:**
- `fileName` (string, обязательный) - Имя файла
- `contentType` (string, обязательный) - MIME-тип файла (например, "image/jpeg", "application/pdf")
- `fileUrl` (string, обязательный) - Абсолютный URL файла

**Ответ:**

Успешный ответ (201 Created):
```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "orderId": "f1e2d3c4-b5a6-7890-1234-567890abcdef",
  "externalCommentId": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
  "commentText": "Спасибо за быструю обработку заказа!",
  "authorProfileId": null,
  "authorName": "Иван Иванов",
  "isFromInternetShop": true,
  "createdAt": "2024-12-16T10:30:00Z",
  "attachments": [
    {
      "id": "c3d4e5f6-a7b8-9012-cdef-123456789012",
      "fileName": "photo.jpg",
      "contentType": "image/jpeg",
      "fileUrl": "https://example.com/files/comments/photo.jpg",
      "createdAt": "2024-12-16T10:30:00Z"
    }
  ]
}
```

**Ошибки:**
- `400 Bad Request` - Заказ не найден или невалидные данные
- `401 Unauthorized` - Пользователь не авторизован
- `500 Internal Server Error` - Внутренняя ошибка сервера

**Пример запроса:**
```bash
curl -X POST "https://api.example.com/api/orders/f1e2d3c4-b5a6-7890-1234-567890abcdef/comments" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "commentText": "Спасибо за быструю обработку заказа!",
    "authorName": "Иван Иванов",
    "attachments": []
  }'
```

**Пример с файлами (после загрузки файла):**
```json
{
  "commentText": "Смотрите фото товара",
  "authorName": "Иван Иванов",
  "attachments": [
    {
      "fileName": "photo.jpg",
      "contentType": "image/jpeg",
      "fileUrl": "https://api.example.com/uploads/orders/f1e2d3c4-b5a6-7890-1234-567890abcdef/comments/photo_20241216103000.jpg"
    }
  ]
}
```

**Важно:**
- Комментарий автоматически получает уникальный `externalCommentId` (GUID)
- Если заказ синхронизирован с FimBiz (есть `fimBizOrderId`), комментарий автоматически отправляется в FimBiz
- Если отправка в FimBiz не удалась, комментарий все равно сохраняется в локальной БД
- Для прикрепления файлов: сначала загрузите файл через эндпоинт `/attachments`, получите `fileUrl`, затем используйте его в поле `attachments` при создании комментария

---

### 3. Загрузить файл для комментария

**POST** `/api/orders/{orderId}/comments/attachments`

Загружает файл для комментария к заказу. Файл сохраняется на сервере, и возвращается информация о файле с URL, который можно использовать при создании комментария.

**Параметры пути:**
- `orderId` (Guid, обязательный) - ID заказа

**Форма данных (multipart/form-data):**
- `file` (IFormFile, обязательный) - Загружаемый файл

**Ограничения:**
- Максимальный размер файла: 50 МБ
- Файл должен быть передан в поле `file`

**Ответ:**

Успешный ответ (200 OK):
```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "fileName": "photo.jpg",
  "contentType": "image/jpeg",
  "fileUrl": "https://api.example.com/uploads/orders/f1e2d3c4-b5a6-7890-1234-567890abcdef/comments/photo_20241216103000.jpg",
  "createdAt": "2024-12-16T10:30:00Z"
}
```

**Ошибки:**
- `400 Bad Request` - Файл не указан, пуст или превышает максимальный размер
- `401 Unauthorized` - Пользователь не авторизован или заказ не принадлежит пользователю
- `404 Not Found` - Заказ не найден
- `500 Internal Server Error` - Внутренняя ошибка сервера

**Пример запроса:**
```bash
curl -X POST "https://api.example.com/api/orders/f1e2d3c4-b5a6-7890-1234-567890abcdef/comments/attachments" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -F "file=@photo.jpg"
```

**JavaScript/TypeScript пример:**
```typescript
async function uploadCommentFile(orderId: string, file: File, token: string) {
  const formData = new FormData();
  formData.append('file', file);

  const response = await fetch(`/api/orders/${orderId}/comments/attachments`, {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`
    },
    body: formData
  });

  if (!response.ok) {
    const error = await response.json();
    throw new Error(error.error || 'Failed to upload file');
  }

  return await response.json();
}
```

**Важно:**
- Файл сохраняется в папку `uploads/orders/{orderId}/comments/`
- Возвращается полный абсолютный URL файла, который можно использовать при создании комментария
- Загруженный файл можно использовать в поле `fileUrl` при создании комментария
- Файлы доступны по URL и могут быть просмотрены/скачаны напрямую

---

### 4. Получить комментарий по ID

**GET** `/api/orders/{orderId}/comments/{commentId}`

Получает конкретный комментарий по его ID.

**Параметры пути:**
- `orderId` (Guid, обязательный) - ID заказа
- `commentId` (Guid, обязательный) - ID комментария

**Ответ:**

Успешный ответ (200 OK):
```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "orderId": "f1e2d3c4-b5a6-7890-1234-567890abcdef",
  "externalCommentId": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
  "commentText": "Спасибо за быструю обработку заказа!",
  "authorProfileId": null,
  "authorUserId": "f1e2d3c4-b5a6-7890-1234-567890abcdef",
  "authorName": "Иван Иванов",
  "isFromInternetShop": true,
  "createdAt": "2024-12-16T10:30:00Z",
  "attachments": []
}
```

**Ошибки:**
- `401 Unauthorized` - Пользователь не авторизован
- `404 Not Found` - Комментарий не найден
- `500 Internal Server Error` - Внутренняя ошибка сервера

**Пример запроса:**
```bash
curl -X GET "https://api.example.com/api/orders/f1e2d3c4-b5a6-7890-1234-567890abcdef/comments/a1b2c3d4-e5f6-7890-abcd-ef1234567890" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

---

### 5. Обновить комментарий

**PUT** `/api/orders/{orderId}/comments/{commentId}`

Обновляет существующий комментарий. **Только автор комментария может его обновлять.** Комментарии из FimBiz обновлять нельзя.

**Параметры пути:**
- `orderId` (Guid, обязательный) - ID заказа
- `commentId` (Guid, обязательный) - ID комментария

**Тело запроса:**
```json
{
  "commentText": "Обновленный текст комментария"
}
```

**Поля:**
- `commentText` (string, обязательный) - Обновленный текст комментария (максимум 5000 символов)

**Ответ:**

Успешный ответ (200 OK):
```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "orderId": "f1e2d3c4-b5a6-7890-1234-567890abcdef",
  "externalCommentId": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
  "commentText": "Обновленный текст комментария",
  "authorProfileId": null,
  "authorUserId": "f1e2d3c4-b5a6-7890-1234-567890abcdef",
  "authorName": "Иван Иванов",
  "isFromInternetShop": true,
  "createdAt": "2024-12-16T10:30:00Z",
  "attachments": []
}
```

**Ошибки:**
- `400 Bad Request` - Комментарий не найден, невалидные данные
- `401 Unauthorized` - Пользователь не авторизован или комментарий не принадлежит текущему пользователю
- `403 Forbidden` - Попытка обновить комментарий из FimBiz (можно обновлять только комментарии из интернет-магазина)
- `404 Not Found` - Комментарий не найден
- `500 Internal Server Error` - Внутренняя ошибка сервера

**Проверки:**
- Комментарий должен быть создан в интернет-магазине (`isFromInternetShop: true`)
- Текущий пользователь должен быть автором комментария (`authorUserId` должен совпадать с ID текущего пользователя)

**Пример запроса:**
```bash
curl -X PUT "https://api.example.com/api/orders/f1e2d3c4-b5a6-7890-1234-567890abcdef/comments/a1b2c3d4-e5f6-7890-abcd-ef1234567890" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "commentText": "Обновленный текст комментария"
  }'
```

**Важно:**
- Обновить можно только текст комментария
- Прикрепленные файлы нельзя изменить (нужно удалить комментарий и создать новый)
- Комментарии из FimBiz обновлять нельзя
- Только автор комментария может его обновлять

---

### 6. Удалить комментарий

**DELETE** `/api/orders/{orderId}/comments/{commentId}`

Удаляет комментарий по его ID.

**Параметры пути:**
- `orderId` (Guid, обязательный) - ID заказа
- `commentId` (Guid, обязательный) - ID комментария

**Ответ:**

Успешный ответ (204 No Content) - комментарий успешно удален

**Ошибки:**
- `401 Unauthorized` - Пользователь не авторизован
- `404 Not Found` - Комментарий не найден
- `500 Internal Server Error` - Внутренняя ошибка сервера

**Пример запроса:**
```bash
curl -X DELETE "https://api.example.com/api/orders/f1e2d3c4-b5a6-7890-1234-567890abcdef/comments/a1b2c3d4-e5f6-7890-abcd-ef1234567890" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

---

## Типы данных

### OrderCommentDto

```typescript
interface OrderCommentDto {
  id: string;                    // GUID комментария
  orderId: string;               // GUID заказа
  externalCommentId: string;     // Внешний ID комментария (GUID из FimBiz или интернет-магазина)
  commentText: string;           // Текст комментария
  authorProfileId?: number;      // ID профиля автора в FimBiz (если комментарий из FimBiz)
  authorUserId?: string;         // ID пользователя автора (если комментарий из интернет-магазина)
  authorName?: string;           // Имя автора (если комментарий из интернет-магазина)
  isFromInternetShop: boolean;   // Флаг: создан ли комментарий в интернет-магазине
  createdAt: string;             // ISO 8601 дата создания
  attachments: OrderCommentAttachmentDto[];
}
```

### OrderCommentAttachmentDto

```typescript
interface OrderCommentAttachmentDto {
  id: string;                    // GUID вложения
  fileName: string;              // Имя файла
  contentType: string;           // MIME-тип файла
  fileUrl: string;               // Абсолютный URL файла
  createdAt: string;             // ISO 8601 дата создания
}
```

### CreateOrderCommentDto

```typescript
interface CreateOrderCommentDto {
  commentText: string;           // Текст комментария (обязательный, максимум 5000 символов)
  authorName?: string;           // Имя автора (опциональный)
  attachments?: CreateOrderCommentAttachmentDto[];  // Прикрепленные файлы (опциональный)
}
```

### CreateOrderCommentAttachmentDto

```typescript
interface CreateOrderCommentAttachmentDto {
  fileName: string;              // Имя файла (обязательный)
  contentType: string;           // MIME-тип файла (обязательный)
  fileUrl: string;               // Абсолютный URL файла (обязательный)
}
```

### UpdateOrderCommentDto

```typescript
interface UpdateOrderCommentDto {
  commentText: string;           // Обновленный текст комментария (обязательный, максимум 5000 символов)
}
```

---

## Особенности работы с комментариями

### Синхронизация с FimBiz

1. **Комментарии из интернет-магазина:**
   - При создании комментария в интернет-магазине он автоматически отправляется в FimBiz (если заказ синхронизирован)
   - Комментарий получает уникальный `externalCommentId` (GUID)
   - Если отправка в FimBiz не удалась, комментарий все равно сохраняется локально

2. **Комментарии из FimBiz:**
   - Комментарии из FimBiz автоматически появляются в интернет-магазине через gRPC
   - Они имеют `isFromInternetShop: false`
   - Автор определяется через `authorProfileId` (ID профиля в FimBiz)
   - Комментарии из FimBiz нельзя редактировать или удалять в интернет-магазине

3. **Обновление комментариев:**
   - Обновлять можно только комментарии, созданные в интернет-магазине (`isFromInternetShop: true`)
   - Обновлять комментарий может только его автор (проверка по `authorUserId`)
   - Обновляется только текст комментария, прикрепленные файлы изменить нельзя

### Прикрепленные файлы

- Файлы передаются как абсолютные URL (например, `https://example.com/files/comments/photo.jpg`)
- Файлы должны быть доступны по указанному URL
- Поддерживаются любые типы файлов (изображения, документы и т.д.)
- MIME-тип файла указывается в поле `contentType`

### Ограничения

- Максимальная длина текста комментария: 5000 символов
- Максимальная длина имени файла: 500 символов
- Максимальная длина URL файла: 2000 символов
- Максимальная длина имени автора: 200 символов

---

## Примеры использования

### JavaScript/TypeScript (Fetch API)

```typescript
// Получить все комментарии к заказу
async function getOrderComments(orderId: string, token: string) {
  const response = await fetch(`/api/orders/${orderId}/comments`, {
    method: 'GET',
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    }
  });
  
  if (!response.ok) {
    throw new Error('Failed to fetch comments');
  }
  
  return await response.json();
}

// Создать комментарий
async function createComment(orderId: string, commentText: string, authorName: string, token: string) {
  const response = await fetch(`/api/orders/${orderId}/comments`, {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      commentText: commentText,
      authorName: authorName,
      attachments: []
    })
  });
  
  if (!response.ok) {
    const error = await response.json();
    throw new Error(error.error || 'Failed to create comment');
  }
  
  return await response.json();
}

// Загрузить файл и создать комментарий с прикрепленным файлом
async function createCommentWithAttachment(
  orderId: string, 
  commentText: string, 
  authorName: string,
  file: File,
  token: string
) {
  // Шаг 1: Загрузить файл
  const formData = new FormData();
  formData.append('file', file);

  const uploadResponse = await fetch(`/api/orders/${orderId}/comments/attachments`, {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`
    },
    body: formData
  });

  if (!uploadResponse.ok) {
    const error = await uploadResponse.json();
    throw new Error(error.error || 'Failed to upload file');
  }

  const uploadedFile = await uploadResponse.json();

  // Шаг 2: Создать комментарий с прикрепленным файлом
  const response = await fetch(`/api/orders/${orderId}/comments`, {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      commentText: commentText,
      authorName: authorName,
      attachments: [
        {
          fileName: uploadedFile.fileName,
          contentType: uploadedFile.contentType,
          fileUrl: uploadedFile.fileUrl
        }
      ]
    })
  });
  
  if (!response.ok) {
    const error = await response.json();
    throw new Error(error.error || 'Failed to create comment');
  }
  
  return await response.json();
}

// Обновить комментарий
async function updateComment(orderId: string, commentId: string, commentText: string, token: string) {
  const response = await fetch(`/api/orders/${orderId}/comments/${commentId}`, {
    method: 'PUT',
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      commentText: commentText
    })
  });
  
  if (!response.ok) {
    const error = await response.json();
    throw new Error(error.error || 'Failed to update comment');
  }
  
  return await response.json();
}

// Удалить комментарий
async function deleteComment(orderId: string, commentId: string, token: string) {
  const response = await fetch(`/api/orders/${orderId}/comments/${commentId}`, {
    method: 'DELETE',
    headers: {
      'Authorization': `Bearer ${token}`
    }
  });
  
  if (!response.ok) {
    throw new Error('Failed to delete comment');
  }
}
```

### React пример

```tsx
import React, { useState, useEffect } from 'react';

interface OrderComment {
  id: string;
  orderId: string;
  commentText: string;
  authorName?: string;
  isFromInternetShop: boolean;
  createdAt: string;
  attachments: Array<{
    id: string;
    fileName: string;
    fileUrl: string;
  }>;
}

function OrderComments({ orderId, token }: { orderId: string; token: string }) {
  const [comments, setComments] = useState<OrderComment[]>([]);
  const [newComment, setNewComment] = useState('');
  const [authorName, setAuthorName] = useState('');
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    loadComments();
  }, [orderId]);

  const loadComments = async () => {
    try {
      const response = await fetch(`/api/orders/${orderId}/comments`, {
        headers: {
          'Authorization': `Bearer ${token}`
        }
      });
      const data = await response.json();
      setComments(data);
    } catch (error) {
      console.error('Failed to load comments:', error);
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoading(true);
    
    try {
      let attachments: Array<{fileName: string; contentType: string; fileUrl: string}> = [];

      // Если выбран файл, сначала загружаем его
      if (selectedFile) {
        const formData = new FormData();
        formData.append('file', selectedFile);

        const uploadResponse = await fetch(`/api/orders/${orderId}/comments/attachments`, {
          method: 'POST',
          headers: {
            'Authorization': `Bearer ${token}`
          },
          body: formData
        });

        if (!uploadResponse.ok) {
          const error = await uploadResponse.json();
          alert(error.error || 'Failed to upload file');
          setLoading(false);
          return;
        }

        const uploadedFile = await uploadResponse.json();
        attachments = [{
          fileName: uploadedFile.fileName,
          contentType: uploadedFile.contentType,
          fileUrl: uploadedFile.fileUrl
        }];
      }

      // Создаем комментарий
      const response = await fetch(`/api/orders/${orderId}/comments`, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          commentText: newComment,
          authorName: authorName,
          attachments: attachments
        })
      });
      
      if (response.ok) {
        setNewComment('');
        setAuthorName('');
        setSelectedFile(null);
        await loadComments(); // Перезагружаем комментарии
      } else {
        const error = await response.json();
        alert(error.error || 'Failed to create comment');
      }
    } catch (error) {
      console.error('Failed to create comment:', error);
      alert('Failed to create comment');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div>
      <h3>Комментарии к заказу</h3>
      
      {/* Форма создания комментария */}
      <form onSubmit={handleSubmit}>
        <div>
          <label>
            Ваше имя:
            <input
              type="text"
              value={authorName}
              onChange={(e) => setAuthorName(e.target.value)}
            />
          </label>
        </div>
        <div>
          <label>
            Комментарий:
            <textarea
              value={newComment}
              onChange={(e) => setNewComment(e.target.value)}
              rows={4}
              required
            />
          </label>
        </div>
        <div>
          <label>
            Прикрепить файл:
            <input
              type="file"
              onChange={(e) => {
                const selectedFile = e.target.files?.[0];
                if (selectedFile) {
                  setSelectedFile(selectedFile);
                }
              }}
            />
          </label>
        </div>
        <button type="submit" disabled={loading}>
          {loading ? 'Отправка...' : 'Отправить комментарий'}
        </button>
      </form>

      {/* Список комментариев */}
      <div>
        {comments.map((comment) => (
          <div key={comment.id} className="comment">
            <div className="comment-header">
              <strong>{comment.authorName || 'Сотрудник FimBiz'}</strong>
              <span className="comment-date">
                {new Date(comment.createdAt).toLocaleString()}
              </span>
              {comment.isFromInternetShop && (
                <span className="badge">Из интернет-магазина</span>
              )}
            </div>
            <div className="comment-text">{comment.commentText}</div>
            {comment.attachments.length > 0 && (
              <div className="comment-attachments">
                {comment.attachments.map((attachment) => (
                  <a
                    key={attachment.id}
                    href={attachment.fileUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                  >
                    {attachment.fileName}
                  </a>
                ))}
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

export default OrderComments;
```

---

## Обработка ошибок

### Типичные ошибки

1. **401 Unauthorized**
   - Причина: Отсутствует или невалидный JWT токен
   - Решение: Проверить авторизацию пользователя

2. **400 Bad Request**
   - Причина: Невалидные данные в запросе или заказ не найден
   - Решение: Проверить формат данных и существование заказа

3. **404 Not Found**
   - Причина: Комментарий или заказ не найден
   - Решение: Проверить корректность ID

4. **500 Internal Server Error**
   - Причина: Внутренняя ошибка сервера
   - Решение: Попробовать позже или обратиться к администратору

### Рекомендации

- Всегда обрабатывайте ошибки в клиентском коде
- Проверяйте статус ответа перед парсингом JSON
- Логируйте ошибки для отладки
- При ошибке отправки комментария в FimBiz, комментарий все равно сохраняется локально

---

## Вопросы и ответы

**Q: Можно ли редактировать комментарий после создания?**
A: В текущей версии редактирование комментариев не предусмотрено. Для изменения нужно удалить старый комментарий и создать новый.

**Q: Как отличить комментарий из интернет-магазина от комментария из FimBiz?**
A: Используйте поле `isFromInternetShop`: `true` - из интернет-магазина, `false` - из FimBiz.

**Q: Что делать, если файл не доступен по указанному URL?**
A: Убедитесь, что файл загружен на сервер и доступен по указанному URL до создания комментария. Сервер не проверяет доступность файла при создании комментария.

**Q: Комментарии синхронизируются в реальном времени?**
A: Комментарии из FimBiz поступают в интернет-магазин через gRPC при их создании. Для обновления списка комментариев на фронтенде рекомендуется периодически обновлять список или использовать механизм polling/WebSocket.

