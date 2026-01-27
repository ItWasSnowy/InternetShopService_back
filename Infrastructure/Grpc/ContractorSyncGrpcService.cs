using Grpc.Core;
using InternetShopService_back.Infrastructure.Grpc.Contractors;
using InternetShopService_back.Infrastructure.SignalR;
using InternetShopService_back.Modules.UserCabinet.DTOs;
using InternetShopService_back.Modules.UserCabinet.Services;
using InternetShopService_back.Modules.UserCabinet.Repositories;
using InternetShopService_back.Shared.Repositories;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Infrastructure.Grpc;

/// <summary>
/// gRPC сервис для обработки запросов от FimBiz
/// </summary>
public class ContractorSyncGrpcService : ContractorSyncService.ContractorSyncServiceBase
{
    private readonly FimBizSessionService _fimBizSessionService;
    private readonly SessionControlService _sessionControlService;
    private readonly ICounterpartyRepository _counterpartyRepository;
    private readonly IDeliveryAddressRepository _deliveryAddressRepository;
    private readonly IShopNotificationService _shopNotificationService;
    private readonly ILogger<ContractorSyncGrpcService> _logger;

    public ContractorSyncGrpcService(
        FimBizSessionService fimBizSessionService,
        SessionControlService sessionControlService,
        ICounterpartyRepository counterpartyRepository,
        IDeliveryAddressRepository deliveryAddressRepository,
        IShopNotificationService shopNotificationService,
        ILogger<ContractorSyncGrpcService> logger)
    {
        _fimBizSessionService = fimBizSessionService;
        _sessionControlService = sessionControlService;
        _counterpartyRepository = counterpartyRepository;
        _deliveryAddressRepository = deliveryAddressRepository;
        _shopNotificationService = shopNotificationService;
        _logger = logger;
    }

    /// <summary>
    /// Получить список активных сессий контрагента
    /// </summary>
    public override async Task<GetActiveSessionsResponse> GetActiveSessions(
        GetActiveSessionsRequest request,
        ServerCallContext context)
    {
        // ===== ДИАГНОСТИЧЕСКОЕ ЛОГИРОВАНИЕ =====
        _logger.LogInformation("=== [CONTRACTOR] ВХОДЯЩИЙ ЗАПРОС GetActiveSessions ===");
        _logger.LogInformation("RemoteAddress: {RemoteAddress}", context.Peer);
        _logger.LogInformation("Request.ContractorId: {ContractorId}", request?.ContractorId);
        // ===== КОНЕЦ ДИАГНОСТИЧЕСКОГО ЛОГИРОВАНИЯ =====

        try
        {
            _logger.LogInformation("Запрос сессий для контрагента {ContractorId} от FimBiz", request.ContractorId);

            var response = await _fimBizSessionService.GetActiveSessionsByContractorIdAsync(request.ContractorId);
            
            _logger.LogInformation("Возвращено {Count} сессий для контрагента {ContractorId}", 
                response.Sessions.Count, request.ContractorId);

            return response;
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении сессий для контрагента {ContractorId}", request.ContractorId);
            throw new RpcException(new Status(StatusCode.Internal, "Internal server error"));
        }
    }

    /// <summary>
    /// Выполнить команду управления сессиями и получить результат
    /// </summary>
    public override async Task<ExecuteSessionControlResponse> ExecuteSessionControl(
        ExecuteSessionControlRequest request,
        ServerCallContext context)
    {
        // ===== ДИАГНОСТИЧЕСКОЕ ЛОГИРОВАНИЕ =====
        _logger.LogInformation("=== [CONTRACTOR] ВХОДЯЩИЙ ЗАПРОС ExecuteSessionControl ===");
        _logger.LogInformation("RemoteAddress: {RemoteAddress}", context.Peer);
        _logger.LogInformation("Request.SessionControl.ContractorId: {ContractorId}", request?.SessionControl?.ContractorId ?? 0);
        _logger.LogInformation("Request.SessionControl.Action: {Action}", request?.SessionControl?.Action);
        // ===== КОНЕЦ ДИАГНОСТИЧЕСКОГО ЛОГИРОВАНИЯ =====

        try
        {
            if (request.SessionControl == null)
            {
                _logger.LogWarning("Получен запрос ExecuteSessionControl без SessionControl");
                throw new RpcException(new Status(StatusCode.InvalidArgument, "SessionControl is required"));
            }

            _logger.LogInformation("Выполнение команды управления сессиями для контрагента {ContractorId} от FimBiz", 
                request.SessionControl.ContractorId);

            var response = await _sessionControlService.ExecuteSessionControlAsync(
                request.SessionControl, 
                context.CancellationToken);

            _logger.LogInformation(
                "Результат выполнения команды для контрагента {ContractorId}: Success={Success}, Message={Message}, DisconnectedCount={DisconnectedCount}",
                request.SessionControl.ContractorId, 
                response.Success, 
                response.Message, 
                response.DisconnectedCount);

            return response;
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при выполнении команды управления сессиями для контрагента {ContractorId}", 
                request.SessionControl?.ContractorId ?? 0);
            throw new RpcException(new Status(StatusCode.Internal, "Internal server error"));
        }
    }

    /// <summary>
    /// Получить адреса доставки контрагента из интернет-магазина
    /// </summary>
    public override async Task<GetContractorDeliveryAddressesResponse> GetContractorDeliveryAddresses(
        GetContractorDeliveryAddressesRequest request,
        ServerCallContext context)
    {
        // ===== ДИАГНОСТИЧЕСКОЕ ЛОГИРОВАНИЕ =====
        _logger.LogInformation("=== [CONTRACTOR] ВХОДЯЩИЙ ЗАПРОС GetContractorDeliveryAddresses ===");
        _logger.LogInformation("RemoteAddress: {RemoteAddress}", context.Peer);
        _logger.LogInformation("Request.ContractorId: {ContractorId}", request?.ContractorId);
        // ===== КОНЕЦ ДИАГНОСТИЧЕСКОГО ЛОГИРОВАНИЯ =====

        try
        {
            _logger.LogInformation("Запрос адресов доставки для контрагента {ContractorId} от FimBiz", request.ContractorId);

            // 2. Найти контрагента по FimBizContractorId
            var counterparty = await _counterpartyRepository.GetByFimBizIdAsync(request.ContractorId);
            
            if (counterparty == null)
            {
                _logger.LogWarning("Контрагент с FimBizContractorId={ContractorId} не найден", 
                    request.ContractorId);
                
                // Возвращаем пустой список адресов
                return new GetContractorDeliveryAddressesResponse();
            }

            // 3. Найти пользовательский аккаунт контрагента
            if (counterparty.UserAccount == null)
            {
                _logger.LogWarning("У контрагента {ContractorId} нет личного кабинета", 
                    request.ContractorId);
                
                // Возвращаем пустой список адресов
                return new GetContractorDeliveryAddressesResponse();
            }

            // 4. Загрузить адреса доставки пользователя
            var addresses = await _deliveryAddressRepository.GetByUserIdAsync(counterparty.UserAccount.Id);

            // 5. Преобразовать в gRPC формат
            var response = new GetContractorDeliveryAddressesResponse();
            
            foreach (var address in addresses)
            {
                response.Addresses.Add(new DeliveryAddress
                {
                    Id = address.Id.ToString(),
                    Address = address.Address,
                    City = address.City ?? string.Empty,
                    Region = address.Region ?? string.Empty,
                    PostalCode = address.PostalCode ?? string.Empty,
                    Apartment = address.Apartment ?? string.Empty,
                    IsDefault = address.IsDefault,
                    DateCreate = ((DateTimeOffset)address.CreatedAt).ToUnixTimeSeconds()
                });
            }

            _logger.LogInformation("Возвращено {Count} адресов для контрагента {ContractorId}", 
                response.Addresses.Count, request.ContractorId);

            return response;
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении адресов для контрагента {ContractorId}", request.ContractorId);
            throw new RpcException(new Status(StatusCode.Internal, "Internal server error"));
        }
    }

    /// <summary>
    /// Создать адрес доставки контрагента
    /// </summary>
    public override async Task<CreateContractorDeliveryAddressResponse> CreateContractorDeliveryAddress(
        CreateContractorDeliveryAddressRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation("=== [CONTRACTOR] ВХОДЯЩИЙ ЗАПРОС CreateContractorDeliveryAddress ===");
        _logger.LogInformation("RemoteAddress: {RemoteAddress}", context.Peer);
        _logger.LogInformation("Request.ContractorId: {ContractorId}, Address: {Address}, IsDefault: {IsDefault}", 
            request.ContractorId, request.Address, request.IsDefault);

        try
        {
            // 2. Валидация данных
            if (string.IsNullOrWhiteSpace(request.Address))
            {
                return new CreateContractorDeliveryAddressResponse
                {
                    Success = false,
                    Message = "Адрес доставки (поле address) не может быть пустым"
                };
            }

            // 3. Найти контрагента по contractor_id (ID из ФимБиз)
            var counterparty = await _counterpartyRepository.GetByFimBizIdAsync(request.ContractorId);
            
            if (counterparty == null)
            {
                _logger.LogWarning("Контрагент с ID {ContractorId} не найден", request.ContractorId);
                return new CreateContractorDeliveryAddressResponse
                {
                    Success = false,
                    Message = $"Контрагент с ID {request.ContractorId} не найден"
                };
            }

            // 4. Найти пользовательский аккаунт контрагента
            if (counterparty.UserAccount == null)
            {
                _logger.LogWarning("У контрагента {ContractorId} нет личного кабинета", request.ContractorId);
                return new CreateContractorDeliveryAddressResponse
                {
                    Success = false,
                    Message = $"У контрагента с ID {request.ContractorId} нет личного кабинета"
                };
            }

            // 5. Создать новый адрес
            var newAddress = new Modules.UserCabinet.Models.DeliveryAddress
            {
                Id = Guid.NewGuid(),
                UserAccountId = counterparty.UserAccount.Id,
                Address = request.Address.Trim(),
                City = request.City ?? string.Empty,
                Region = request.Region ?? string.Empty,
                PostalCode = request.PostalCode ?? string.Empty,
                Apartment = request.Apartment ?? string.Empty,
                IsDefault = request.IsDefault,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // 6. Если is_default = true, снять флаг is_default у всех существующих адресов
            if (request.IsDefault)
            {
                await _deliveryAddressRepository.SetDefaultAsync(counterparty.UserAccount.Id, newAddress.Id);
            }

            newAddress = await _deliveryAddressRepository.CreateAsync(newAddress);

            _logger.LogInformation("Создан адрес доставки для контрагента {ContractorId}, AddressId: {AddressId}", 
                request.ContractorId, newAddress.Id);

            await _shopNotificationService.DeliveryAddressCreated(counterparty.Id, MapToDto(newAddress));

            // 7. Вернуть созданный адрес
            return new CreateContractorDeliveryAddressResponse
            {
                Success = true,
                Message = "Адрес доставки успешно создан",
                Address = new DeliveryAddress
                {
                    Id = newAddress.Id.ToString(),
                    Address = newAddress.Address,
                    City = newAddress.City ?? string.Empty,
                    Region = newAddress.Region ?? string.Empty,
                    PostalCode = newAddress.PostalCode ?? string.Empty,
                    Apartment = newAddress.Apartment ?? string.Empty,
                    IsDefault = newAddress.IsDefault,
                    DateCreate = ((DateTimeOffset)newAddress.CreatedAt).ToUnixTimeSeconds()
                }
            };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при создании адреса для контрагента {ContractorId}", request.ContractorId);
            return new CreateContractorDeliveryAddressResponse
            {
                Success = false,
                Message = $"Ошибка при создании адреса: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Обновить адрес доставки контрагента
    /// </summary>
    public override async Task<UpdateContractorDeliveryAddressResponse> UpdateContractorDeliveryAddress(
        UpdateContractorDeliveryAddressRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation("=== [CONTRACTOR] ВХОДЯЩИЙ ЗАПРОС UpdateContractorDeliveryAddress ===");
        _logger.LogInformation("RemoteAddress: {RemoteAddress}", context.Peer);
        _logger.LogInformation("Request.ContractorId: {ContractorId}, AddressId: {AddressId}, Address: {Address}, IsDefault: {IsDefault}", 
            request.ContractorId, request.AddressId, request.Address, request.IsDefault);

        try
        {
            // 2. Валидация данных
            if (string.IsNullOrWhiteSpace(request.Address))
            {
                return new UpdateContractorDeliveryAddressResponse
                {
                    Success = false,
                    Message = "Адрес доставки (поле address) не может быть пустым"
                };
            }

            // 3. Парсинг ID адреса
            if (!Guid.TryParse(request.AddressId, out var addressId))
            {
                return new UpdateContractorDeliveryAddressResponse
                {
                    Success = false,
                    Message = $"Неверный формат ID адреса: {request.AddressId}"
                };
            }

            // 4. Найти адрес
            var address = await _deliveryAddressRepository.GetByIdAsync(addressId);
            
            if (address == null)
            {
                return new UpdateContractorDeliveryAddressResponse
                {
                    Success = false,
                    Message = $"Адрес с ID {request.AddressId} не найден"
                };
            }

            // 5. Проверить, что адрес принадлежит контрагенту
            var counterparty = await _counterpartyRepository.GetByFimBizIdAsync(request.ContractorId);
            
            if (counterparty == null || counterparty.UserAccount == null || 
                address.UserAccountId != counterparty.UserAccount.Id)
            {
                return new UpdateContractorDeliveryAddressResponse
                {
                    Success = false,
                    Message = $"Адрес с ID {request.AddressId} не найден у контрагента {request.ContractorId}"
                };
            }

            // 6. Если is_default = true, снять флаг у других адресов
            if (request.IsDefault && !address.IsDefault)
            {
                await _deliveryAddressRepository.SetDefaultAsync(counterparty.UserAccount.Id, addressId);
            }

            // 7. Обновить адрес
            address.Address = request.Address.Trim();
            address.City = request.City ?? string.Empty;
            address.Region = request.Region ?? string.Empty;
            address.PostalCode = request.PostalCode ?? string.Empty;
            address.Apartment = request.Apartment ?? string.Empty;
            address.IsDefault = request.IsDefault;
            address.UpdatedAt = DateTime.UtcNow;

            address = await _deliveryAddressRepository.UpdateAsync(address);

            _logger.LogInformation("Обновлен адрес доставки для контрагента {ContractorId}, AddressId: {AddressId}", 
                request.ContractorId, request.AddressId);

            await _shopNotificationService.DeliveryAddressUpdated(counterparty.Id, MapToDto(address));

            return new UpdateContractorDeliveryAddressResponse
            {
                Success = true,
                Message = "Адрес доставки успешно обновлен",
                Address = new DeliveryAddress
                {
                    Id = address.Id.ToString(),
                    Address = address.Address,
                    City = address.City ?? string.Empty,
                    Region = address.Region ?? string.Empty,
                    PostalCode = address.PostalCode ?? string.Empty,
                    Apartment = address.Apartment ?? string.Empty,
                    IsDefault = address.IsDefault,
                    DateCreate = ((DateTimeOffset)address.CreatedAt).ToUnixTimeSeconds()
                }
            };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обновлении адреса для контрагента {ContractorId}", request.ContractorId);
            return new UpdateContractorDeliveryAddressResponse
            {
                Success = false,
                Message = $"Ошибка при обновлении адреса: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Удалить адрес доставки контрагента
    /// </summary>
    public override async Task<DeleteContractorDeliveryAddressResponse> DeleteContractorDeliveryAddress(
        DeleteContractorDeliveryAddressRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation("=== [CONTRACTOR] ВХОДЯЩИЙ ЗАПРОС DeleteContractorDeliveryAddress ===");
        _logger.LogInformation("RemoteAddress: {RemoteAddress}", context.Peer);
        _logger.LogInformation("Request.ContractorId: {ContractorId}, AddressId: {AddressId}", 
            request.ContractorId, request.AddressId);

        try
        {
            // 2. Валидация данных
            if (string.IsNullOrWhiteSpace(request.AddressId))
            {
                return new DeleteContractorDeliveryAddressResponse
                {
                    Success = false,
                    Message = "Id адреса (поле address_id) не может быть пустым"
                };
            }

            if (!Guid.TryParse(request.AddressId, out var addressId))
            {
                return new DeleteContractorDeliveryAddressResponse
                {
                    Success = false,
                    Message = $"Неверный формат ID адреса: {request.AddressId}"
                };
            }

            // 3. Найти адрес
            var address = await _deliveryAddressRepository.GetByIdAsync(addressId);
            
            if (address == null)
            {
                return new DeleteContractorDeliveryAddressResponse
                {
                    Success = false,
                    Message = $"Адрес с ID {request.AddressId} не найден"
                };
            }

            // 4. Проверить, что адрес принадлежит контрагенту
            var counterparty = await _counterpartyRepository.GetByFimBizIdAsync(request.ContractorId);
            
            if (counterparty == null || counterparty.UserAccount == null || 
                address.UserAccountId != counterparty.UserAccount.Id)
            {
                return new DeleteContractorDeliveryAddressResponse
                {
                    Success = false,
                    Message = $"Адрес с ID {request.AddressId} не найден у контрагента {request.ContractorId}"
                };
            }

            // 5. Удалить адрес
            var deleted = await _deliveryAddressRepository.DeleteAsync(addressId);

            if (deleted)
            {
                _logger.LogInformation("Удален адрес доставки для контрагента {ContractorId}, AddressId: {AddressId}", 
                    request.ContractorId, request.AddressId);

                await _shopNotificationService.DeliveryAddressDeleted(counterparty.Id, addressId);
            }

            return new DeleteContractorDeliveryAddressResponse
            {
                Success = deleted,
                Message = deleted ? "Адрес доставки успешно удален" : "Не удалось удалить адрес"
            };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при удалении адреса для контрагента {ContractorId}", request.ContractorId);
            return new DeleteContractorDeliveryAddressResponse
            {
                Success = false,
                Message = $"Ошибка при удалении адреса: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Синхронизировать все адреса доставки контрагента (полная замена)
    /// </summary>
    public override async Task<SyncContractorDeliveryAddressesResponse> SyncContractorDeliveryAddresses(
        SyncContractorDeliveryAddressesRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation("=== [CONTRACTOR] ВХОДЯЩИЙ ЗАПРОС SyncContractorDeliveryAddresses ===");
        _logger.LogInformation("RemoteAddress: {RemoteAddress}", context.Peer);
        _logger.LogInformation("Request.ContractorId: {ContractorId}, AddressesCount: {Count}", 
            request.ContractorId, request.Addresses.Count);

        try
        {
            // 2. Найти контрагента
            var counterparty = await _counterpartyRepository.GetByFimBizIdAsync(request.ContractorId);
            
            if (counterparty == null)
            {
                return new SyncContractorDeliveryAddressesResponse
                {
                    Success = false,
                    Message = $"Контрагент с ID {request.ContractorId} не найден"
                };
            }

            // 3. Найти пользовательский аккаунт контрагента
            if (counterparty.UserAccount == null)
            {
                return new SyncContractorDeliveryAddressesResponse
                {
                    Success = false,
                    Message = $"У контрагента с ID {request.ContractorId} нет личного кабинета"
                };
            }

            // 4. Удалить все существующие адреса контрагента
            var existingAddresses = await _deliveryAddressRepository.GetByUserIdAsync(counterparty.UserAccount.Id);
            
            foreach (var addr in existingAddresses)
            {
                await _deliveryAddressRepository.DeleteAsync(addr.Id);
            }

            // 5. Создать новые адреса из запроса
            var newAddresses = new List<Modules.UserCabinet.Models.DeliveryAddress>();
            var hasDefault = false;

            foreach (var addr in request.Addresses)
            {
                if (string.IsNullOrWhiteSpace(addr.Address))
                {
                    _logger.LogWarning("Пропущен пустой адрес при синхронизации для контрагента {ContractorId}", 
                        request.ContractorId);
                    continue;
                }

                // Проверка, что только один адрес может быть по умолчанию
                var isDefault = addr.IsDefault;
                if (isDefault && hasDefault)
                {
                    _logger.LogWarning("Несколько адресов помечены как default для контрагента {ContractorId}. Используется первый.", 
                        request.ContractorId);
                    isDefault = false;
                }

                if (isDefault)
                {
                    hasDefault = true;
                }

                var newAddress = new Modules.UserCabinet.Models.DeliveryAddress
                {
                    Id = Guid.NewGuid(),
                    UserAccountId = counterparty.UserAccount.Id,
                    Address = addr.Address.Trim(),
                    City = addr.City ?? string.Empty,
                    Region = addr.Region ?? string.Empty,
                    PostalCode = addr.PostalCode ?? string.Empty,
                    Apartment = addr.Apartment ?? string.Empty,
                    IsDefault = isDefault,
                    CreatedAt = addr.DateCreate > 0 
                        ? DateTimeOffset.FromUnixTimeSeconds(addr.DateCreate).DateTime 
                        : DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                newAddresses.Add(newAddress);
            }

            // Сохранить все адреса
            foreach (var addr in newAddresses)
            {
                await _deliveryAddressRepository.CreateAsync(addr);
            }

            _logger.LogInformation("Синхронизировано {Count} адресов доставки для контрагента {ContractorId}", 
                newAddresses.Count, request.ContractorId);

            // 6. Вернуть синхронизированные адреса с ID из интернет-магазина
            var syncedAddresses = newAddresses.Select(a => new DeliveryAddress
            {
                Id = a.Id.ToString(),
                Address = a.Address,
                City = a.City ?? string.Empty,
                Region = a.Region ?? string.Empty,
                PostalCode = a.PostalCode ?? string.Empty,
                Apartment = a.Apartment ?? string.Empty,
                IsDefault = a.IsDefault,
                DateCreate = ((DateTimeOffset)a.CreatedAt).ToUnixTimeSeconds()
            }).ToList();

            return new SyncContractorDeliveryAddressesResponse
            {
                Success = true,
                Message = $"Успешно синхронизировано {syncedAddresses.Count} адресов",
                Addresses = { syncedAddresses }
            };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при синхронизации адресов для контрагента {ContractorId}", request.ContractorId);
            return new SyncContractorDeliveryAddressesResponse
            {
                Success = false,
                Message = $"Ошибка при синхронизации адресов: {ex.Message}"
            };
        }
    }

    private static DeliveryAddressDto MapToDto(Modules.UserCabinet.Models.DeliveryAddress address)
    {
        return new DeliveryAddressDto
        {
            Id = address.Id,
            Address = address.Address ?? string.Empty,
            City = address.City,
            Region = address.Region,
            PostalCode = address.PostalCode,
            Apartment = address.Apartment,
            IsDefault = address.IsDefault,
            CreatedAt = address.CreatedAt,
            UpdatedAt = address.UpdatedAt
        };
    }
}

