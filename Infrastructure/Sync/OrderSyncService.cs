using InternetShopService_back.Data;
using InternetShopService_back.Infrastructure.Grpc;
using InternetShopService_back.Infrastructure.Grpc.Orders;
using InternetShopService_back.Modules.OrderManagement.Models;
using InternetShopService_back.Modules.OrderManagement.Repositories;
using InternetShopService_back.Modules.UserCabinet.Repositories;
using InternetShopService_back.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Grpc.Core;
using LocalOrder = InternetShopService_back.Modules.OrderManagement.Models.Order;
using LocalDeliveryType = InternetShopService_back.Modules.OrderManagement.Models.DeliveryType;
using GrpcDeliveryType = InternetShopService_back.Infrastructure.Grpc.Orders.DeliveryType;
using GrpcOrderItem = InternetShopService_back.Infrastructure.Grpc.Orders.OrderItem;
using AttachedFile = InternetShopService_back.Infrastructure.Grpc.Orders.AttachedFile;

namespace InternetShopService_back.Infrastructure.Sync;

/// <summary>
/// Фоновая служба для периодической синхронизации неотправленных заказов с FimBiz
/// </summary>
public class OrderSyncService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrderSyncService> _logger;
    private readonly IConfiguration _configuration;

    public OrderSyncService(
        IServiceProvider serviceProvider,
        ILogger<OrderSyncService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Проверяем, включена ли автоматическая синхронизация заказов
        var enableAutoSync = _configuration.GetValue<bool>("FimBiz:EnableAutoSync", true);
        if (!enableAutoSync)
        {
            _logger.LogInformation("Автоматическая синхронизация заказов с FimBiz отключена");
            return;
        }

        var syncIntervalMinutes = _configuration.GetValue<int>("FimBiz:OrderSyncIntervalMinutes", 5);
        _logger.LogInformation("Служба синхронизации заказов запущена. Интервал проверки: {IntervalMinutes} минут", syncIntervalMinutes);

        // Небольшая задержка перед первым запуском, чтобы приложение полностью запустилось
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncUnsyncedOrdersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при синхронизации заказов");
            }

            // Ждем до следующей проверки
            await Task.Delay(TimeSpan.FromMinutes(syncIntervalMinutes), stoppingToken);
        }
    }

    private async Task SyncUnsyncedOrdersAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var userAccountRepository = scope.ServiceProvider.GetRequiredService<IUserAccountRepository>();
        var counterpartyRepository = scope.ServiceProvider.GetRequiredService<ICounterpartyRepository>();
        var shopRepository = scope.ServiceProvider.GetRequiredService<IShopRepository>();
        var deliveryAddressRepository = scope.ServiceProvider.GetRequiredService<IDeliveryAddressRepository>();
        var fimBizGrpcClient = scope.ServiceProvider.GetRequiredService<IFimBizGrpcClient>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        try
        {
            // Получаем неотправленные заказы (по умолчанию до 100 штук за раз)
            var batchSize = _configuration.GetValue<int>("FimBiz:OrderSyncBatchSize", 100);
            var unsyncedOrders = await orderRepository.GetUnsyncedOrdersAsync(batchSize);

            if (!unsyncedOrders.Any())
            {
                _logger.LogDebug("Нет неотправленных заказов для синхронизации");
                return;
            }

            _logger.LogInformation("Найдено {Count} неотправленных заказов. Начинаю синхронизацию...", unsyncedOrders.Count);

            int successCount = 0;
            int errorCount = 0;

            foreach (var order in unsyncedOrders)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    // Получаем UserAccount для заказа
                    var userAccount = await userAccountRepository.GetByIdAsync(order.UserAccountId);
                    if (userAccount == null)
                    {
                        _logger.LogWarning("Не найден UserAccount для заказа {OrderId}", order.Id);
                        errorCount++;
                        continue;
                    }

                    // Отправляем заказ в FimBiz
                    var sent = await SendOrderToFimBizAsync(
                        order,
                        userAccount,
                        orderRepository,
                        counterpartyRepository,
                        shopRepository,
                        deliveryAddressRepository,
                        fimBizGrpcClient,
                        dbContext);

                    if (sent)
                    {
                        successCount++;
                        _logger.LogInformation("Заказ {OrderId} успешно отправлен в FimBiz", order.Id);
                    }
                    else
                    {
                        errorCount++;
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    _logger.LogError(ex, "Ошибка при отправке заказа {OrderId} в FimBiz", order.Id);
                }
            }

            _logger.LogInformation(
                "Синхронизация заказов завершена. Успешно: {SuccessCount}, Ошибок: {ErrorCount}",
                successCount, errorCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении списка неотправленных заказов");
        }
    }

    private async Task<bool> SendOrderToFimBizAsync(
        LocalOrder order,
        InternetShopService_back.Modules.UserCabinet.Models.UserAccount userAccount,
        IOrderRepository orderRepository,
        ICounterpartyRepository counterpartyRepository,
        IShopRepository shopRepository,
        IDeliveryAddressRepository deliveryAddressRepository,
        IFimBizGrpcClient fimBizGrpcClient,
        ApplicationDbContext context)
    {
        try
        {
            // Убеждаемся, что Items загружены
            if (order.Items == null || !order.Items.Any())
            {
                await context.Entry(order).Collection(o => o.Items).LoadAsync();
                
                if (order.Items == null || !order.Items.Any())
                {
                    _logger.LogWarning("Заказ {OrderId} не содержит Items", order.Id);
                    return false;
                }
            }

            // Загружаем прикрепленные файлы
            if (order.Attachments == null || !order.Attachments.Any())
            {
                await context.Entry(order).Collection(o => o.Attachments).LoadAsync();
            }

            // Получаем контрагента
            var counterparty = await counterpartyRepository.GetByIdAsync(order.CounterpartyId);
            if (counterparty == null || !counterparty.FimBizContractorId.HasValue || counterparty.FimBizContractorId.Value <= 0)
            {
                _logger.LogWarning("Не удалось отправить заказ {OrderId}: контрагент не имеет FimBizContractorId", order.Id);
                return false;
            }

            // Получаем магазин
            var shop = await shopRepository.GetByIdAsync(userAccount.ShopId);
            if (shop == null || shop.FimBizCompanyId <= 0)
            {
                _logger.LogWarning("Не удалось отправить заказ {OrderId}: магазин не найден или неверный FimBizCompanyId", order.Id);
                return false;
            }

            // Формируем адрес доставки
            string deliveryAddress = string.Empty;
            if (order.DeliveryAddressId.HasValue)
            {
                var address = await deliveryAddressRepository.GetByIdAsync(order.DeliveryAddressId.Value);
                if (address != null)
                {
                    var addressParts = new List<string>();
                    if (!string.IsNullOrEmpty(address.Region)) addressParts.Add(address.Region);
                    if (!string.IsNullOrEmpty(address.City)) addressParts.Add(address.City);
                    addressParts.Add(address.Address);
                    if (!string.IsNullOrEmpty(address.Apartment)) addressParts.Add($"кв. {address.Apartment}");
                    if (!string.IsNullOrEmpty(address.PostalCode)) addressParts.Add($"индекс: {address.PostalCode}");
                    deliveryAddress = string.Join(", ", addressParts);
                }
            }

            if (string.IsNullOrEmpty(deliveryAddress) && order.DeliveryType == LocalDeliveryType.Pickup)
            {
                deliveryAddress = "Самовывоз";
            }

            // Преобразуем DeliveryType из нашей модели в gRPC
            var deliveryType = order.DeliveryType switch
            {
                LocalDeliveryType.Pickup => GrpcDeliveryType.SelfPickup,
                LocalDeliveryType.SellerDelivery => GrpcDeliveryType.CompanyDelivery,
                LocalDeliveryType.Carrier => GrpcDeliveryType.TransportCompany,
                _ => (GrpcDeliveryType)0 // DeliveryTypeUnspecified = 0
            };

            // Создаем запрос для FimBiz
            var createOrderRequest = new CreateOrderRequest
            {
                CompanyId = shop.FimBizCompanyId,
                ExternalOrderId = order.Id.ToString(),
                ContractorId = counterparty.FimBizContractorId.Value,
                DeliveryAddress = deliveryAddress,
                DeliveryType = deliveryType
            };

            if (shop.FimBizOrganizationId.HasValue && shop.FimBizOrganizationId.Value > 0)
            {
                createOrderRequest.OrganizationId = shop.FimBizOrganizationId.Value;
            }

            // Добавляем позиции заказа
            foreach (var item in order.Items)
            {
                var grpcItem = new GrpcOrderItem
                {
                    Name = item.NomenclatureName,
                    Quantity = item.Quantity,
                    Price = (long)(item.Price * 100), // Цена в копейках
                    IsAvailable = true,
                    RequiresManufacturing = false
                };

                // Логируем исходное значение NomenclatureId
                _logger.LogDebug("Обработка позиции заказа: NomenclatureId={NomenclatureId}, NomenclatureName={NomenclatureName}", 
                    item.NomenclatureId, item.NomenclatureName);

                if (item.NomenclatureId != Guid.Empty)
                {
                    // Извлекаем число из Guid для FimBiz
                    // Guid формат: "00000000-0000-0000-0000-000000000167"
                    // Реальный ID номенклатуры в FimBiz хранится в последней части Guid после последнего дефиса
                    // Например: "00000000-0000-0000-0000-000000000167" -> извлекаем "000000000167" -> убираем нули -> "167"
                    var guidString = item.NomenclatureId.ToString();
                    var parts = guidString.Split('-');
                    
                    if (parts.Length == 5)
                    {
                        // Берем последнюю часть Guid (после последнего дефиса)
                        var lastPart = parts[4]; // "000000000019"
                        
                        // Парсим как hex, так как Guid хранит значения в hex формате
                        // Например: "000000000019" (hex) = 25 (decimal)
                        if (int.TryParse(lastPart, System.Globalization.NumberStyles.HexNumber, null, out var nomenclatureIdInt32) && nomenclatureIdInt32 > 0)
                        {
                            grpcItem.NomenclatureId = nomenclatureIdInt32;
                            
                            _logger.LogInformation("Конвертация NomenclatureId: Guid={Guid} -> int32={Int32} (из последней части '{LastPart}' как hex)", 
                                item.NomenclatureId, nomenclatureIdInt32, lastPart);
                        }
                        else
                        {
                            // Если не удалось распарсить как hex, пробуем как decimal (для обратной совместимости)
                            if (int.TryParse(lastPart.TrimStart('0'), out var decimalValue) && decimalValue > 0)
                            {
                                grpcItem.NomenclatureId = decimalValue;
                                _logger.LogInformation("Конвертация NomenclatureId (decimal): Guid={Guid} -> int32={Int32} (из последней части '{LastPart}')", 
                                    item.NomenclatureId, decimalValue, lastPart);
                            }
                            else
                            {
                                _logger.LogWarning("Не удалось извлечь NomenclatureId из Guid. Guid={Guid}, LastPart={LastPart}. Поле не будет отправлено в FimBiz", 
                                    item.NomenclatureId, lastPart);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Неверный формат Guid для NomenclatureId. Guid={Guid}, GuidString={GuidString}. Поле не будет отправлено в FimBiz", 
                            item.NomenclatureId, guidString);
                    }
                }
                else
                {
                    _logger.LogWarning("Позиция заказа имеет пустой NomenclatureId (Guid.Empty). Поле не будет отправлено в FimBiz. OrderId={OrderId}, ItemName={ItemName}", 
                        order.Id, item.NomenclatureName);
                }

                // Логируем финальное значение NomenclatureId, которое будет отправлено в FimBiz
                if (grpcItem.HasNomenclatureId && grpcItem.NomenclatureId > 0)
                {
                    _logger.LogInformation("Отправка позиции в FimBiz: NomenclatureId={NomenclatureId}, Name={Name}, Quantity={Quantity}, Price={Price}", 
                        grpcItem.NomenclatureId, grpcItem.Name, grpcItem.Quantity, grpcItem.Price);
                }
                else
                {
                    _logger.LogWarning("Позиция заказа отправляется в FimBiz без NomenclatureId: Name={Name}, Quantity={Quantity}, Price={Price}", 
                        grpcItem.Name, grpcItem.Quantity, grpcItem.Price);
                }

                createOrderRequest.Items.Add(grpcItem);
            }

            // Добавляем прикрепленные файлы (если есть)
            if (order.Attachments != null && order.Attachments.Any())
            {
                var baseUrl = _configuration["AppSettings:BaseUrl"] 
                    ?? _configuration["AppSettings:PublicUrl"]
                    ?? throw new InvalidOperationException("AppSettings:BaseUrl или AppSettings:PublicUrl должен быть настроен для отправки файлов");

                foreach (var attachment in order.Attachments)
                {
                    // Формируем абсолютный URL для файла
                    var fileUrl = GetPublicFileUrl(baseUrl, attachment.FilePath);
                    
                    var attachedFile = new AttachedFile
                    {
                        FileName = attachment.FileName,
                        ContentType = attachment.ContentType,
                        Url = fileUrl
                    };

                    createOrderRequest.AttachedFiles.Add(attachedFile);
                }

                _logger.LogInformation(
                    "Добавлено {FilesCount} прикрепленных файлов к заказу {OrderId}",
                    order.Attachments.Count, order.Id);
            }

            _logger.LogInformation(
                "Отправка заказа {OrderId} в FimBiz. CompanyId: {CompanyId}, ContractorId: {ContractorId}, ItemsCount: {ItemsCount}, FilesCount: {FilesCount}",
                order.Id, shop.FimBizCompanyId, counterparty.FimBizContractorId.Value, order.Items.Count, 
                order.Attachments?.Count ?? 0);

            // Отправляем в FimBiz
            var response = await fimBizGrpcClient.CreateOrderAsync(createOrderRequest);

            if (response.Success && response.Order != null)
            {
                // Обновляем заказ с FimBizOrderId
                order.FimBizOrderId = response.Order.OrderId;
                order.OrderNumber = response.Order.OrderNumber;
                order.SyncedWithFimBizAt = DateTime.UtcNow;

                if (!string.IsNullOrEmpty(response.Order.TrackingNumber))
                {
                    order.TrackingNumber = response.Order.TrackingNumber;
                }

                await orderRepository.UpdateAsync(order);

                _logger.LogInformation(
                    "Заказ {OrderId} успешно отправлен в FimBiz. FimBizOrderId: {FimBizOrderId}, OrderNumber: {OrderNumber}",
                    order.Id, order.FimBizOrderId, order.OrderNumber);

                return true;
            }
            else
            {
                _logger.LogWarning(
                    "Не удалось создать заказ {OrderId} в FimBiz: {Message}",
                    order.Id, response.Message ?? "Неизвестная ошибка");
                return false;
            }
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex,
                "Ошибка gRPC при отправке заказа {OrderId} в FimBiz. StatusCode: {StatusCode}, Detail: {Detail}",
                order.Id, ex.StatusCode, ex.Status.Detail);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Неожиданная ошибка при отправке заказа {OrderId} в FimBiz", order.Id);
            return false;
        }
    }

    /// <summary>
    /// Преобразует локальный путь файла в абсолютный публичный URL
    /// </summary>
    private static string GetPublicFileUrl(string baseUrl, string filePath)
    {
        // Убираем базовый URL из начала, если он есть
        baseUrl = baseUrl.TrimEnd('/');
        
        // Если filePath уже является абсолютным URL, возвращаем его
        if (filePath.StartsWith("http://") || filePath.StartsWith("https://"))
        {
            return filePath;
        }

        // Убираем начальные слэши из filePath
        var relativePath = filePath.TrimStart('/', '\\').Replace('\\', '/');
        
        return $"{baseUrl}/{relativePath}";
    }
}

