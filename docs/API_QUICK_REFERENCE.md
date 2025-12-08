# API - Краткая справка

## Базовый URL
```
/api
```

## Авторизация
Все защищенные эндпоинты требуют заголовок:
```
Authorization: Bearer {accessToken}
```

---

## Авторизация

| Метод | Endpoint | Описание | Auth |
|-------|----------|----------|------|
| POST | `/api/auth/request-code` | Запрос кода по звонку | ❌ |
| POST | `/api/auth/verify-code` | Проверка кода | ❌ |
| POST | `/api/auth/set-password` | Установка пароля | ✅ |
| POST | `/api/auth/change-password` | Смена пароля | ✅ |
| POST | `/api/auth/login` | Вход по паролю | ❌ |
| POST | `/api/auth/refresh` | Обновление токена | ❌ |
| POST | `/api/auth/logout` | Выход | ✅ |

---

## Корзина

| Метод | Endpoint | Описание | Auth |
|-------|----------|----------|------|
| GET | `/api/cart` | Получить корзину | ✅ |
| POST | `/api/cart/add` | Добавить товар | ✅ |
| PUT | `/api/cart/{itemId}` | Обновить количество | ✅ |
| DELETE | `/api/cart/{itemId}` | Удалить товар | ✅ |
| DELETE | `/api/cart/clear` | Очистить корзину | ✅ |
| POST | `/api/cart/create-order` | Создать заказ | ✅ |

---

## Адреса доставки

| Метод | Endpoint | Описание | Auth |
|-------|----------|----------|------|
| GET | `/api/deliveryaddress` | Список адресов | ✅ |
| GET | `/api/deliveryaddress/{id}` | Адрес по ID | ✅ |
| GET | `/api/deliveryaddress/default` | Адрес по умолчанию | ✅ |
| POST | `/api/deliveryaddress` | Создать адрес | ✅ |
| PUT | `/api/deliveryaddress/{id}` | Обновить адрес | ✅ |
| DELETE | `/api/deliveryaddress/{id}` | Удалить адрес | ✅ |
| PUT | `/api/deliveryaddress/{id}/set-default` | Установить по умолчанию | ✅ |

---

## Грузополучатели

| Метод | Endpoint | Описание | Auth |
|-------|----------|----------|------|
| GET | `/api/cargoreceiver` | Список получателей | ✅ |
| GET | `/api/cargoreceiver/{id}` | Получатель по ID | ✅ |
| GET | `/api/cargoreceiver/default` | Получатель по умолчанию | ✅ |
| POST | `/api/cargoreceiver` | Создать получателя | ✅ |
| PUT | `/api/cargoreceiver/{id}` | Обновить получателя | ✅ |
| DELETE | `/api/cargoreceiver/{id}` | Удалить получателя | ✅ |
| PUT | `/api/cargoreceiver/{id}/set-default` | Установить по умолчанию | ✅ |

---

## Контрагент

| Метод | Endpoint | Описание | Auth |
|-------|----------|----------|------|
| GET | `/api/counterparty/current` | Данные контрагента | ✅ |
| GET | `/api/counterparty/discounts` | Скидки контрагента | ✅ |
| POST | `/api/counterparty/sync` | Синхронизация с FimBiz | ✅ |

---

## Сессии

| Метод | Endpoint | Описание | Auth |
|-------|----------|----------|------|
| GET | `/api/sessions` | Список активных сессий | ✅ |
| POST | `/api/sessions/{sessionId}/deactivate` | Деактивировать сессию | ✅ |
| POST | `/api/sessions/deactivate` | Деактивировать несколько | ✅ |

---

## Типы данных

### DeliveryType
- `SelfPickup` - Самовывоз
- `Carrier` - Транспортная компания
- `ShopDelivery` - Доставка магазином

### CounterpartyType
- `B2B` - Юридическое лицо
- `B2C` - Физическое лицо

---

## Формат номера телефона
```
7XXXXXXXXXX (11 цифр, начинается с 7)
Пример: 79991234567
```

---

## Коды ответов

- `200 OK` - Успешно
- `201 Created` - Создано
- `204 No Content` - Успешно без тела
- `400 Bad Request` - Неверный запрос
- `401 Unauthorized` - Требуется авторизация
- `404 Not Found` - Не найдено
- `500 Internal Server Error` - Ошибка сервера

---

Подробная документация: [API_DOCUMENTATION.md](./API_DOCUMENTATION.md)

