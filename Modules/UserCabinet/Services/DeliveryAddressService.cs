using InternetShopService_back.Modules.UserCabinet.DTOs;
using InternetShopService_back.Modules.UserCabinet.Models;
using InternetShopService_back.Modules.UserCabinet.Repositories;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Modules.UserCabinet.Services;

public class DeliveryAddressService : IDeliveryAddressService
{
    private readonly IDeliveryAddressRepository _addressRepository;
    private readonly IUserAccountRepository _userAccountRepository;
    private readonly ILogger<DeliveryAddressService> _logger;

    public DeliveryAddressService(
        IDeliveryAddressRepository addressRepository,
        IUserAccountRepository userAccountRepository,
        ILogger<DeliveryAddressService> logger)
    {
        _addressRepository = addressRepository;
        _userAccountRepository = userAccountRepository;
        _logger = logger;
    }

    public async Task<List<DeliveryAddressDto>> GetAddressesAsync(Guid userId)
    {
        var addresses = await _addressRepository.GetByUserIdAsync(userId);
        return addresses.Select(MapToDto).ToList();
    }

    public async Task<DeliveryAddressDto?> GetAddressAsync(Guid userId, Guid addressId)
    {
        var address = await _addressRepository.GetByIdAsync(addressId);
        if (address == null || address.UserAccountId != userId)
            return null;

        return MapToDto(address);
    }

    public async Task<DeliveryAddressDto?> GetDefaultAddressAsync(Guid userId)
    {
        var address = await _addressRepository.GetDefaultByUserIdAsync(userId);
        return address == null ? null : MapToDto(address);
    }

    public async Task<DeliveryAddressDto> CreateAddressAsync(Guid userId, CreateDeliveryAddressDto dto)
    {
        var userAccount = await _userAccountRepository.GetByIdAsync(userId);
        if (userAccount == null)
            throw new InvalidOperationException("Пользователь не найден");

        var address = new DeliveryAddress
        {
            Id = Guid.NewGuid(),
            UserAccountId = userId,
            Address = dto.Address,
            City = dto.City,
            Region = dto.Region,
            PostalCode = dto.PostalCode,
            Apartment = dto.Apartment,
            IsDefault = dto.IsDefault
        };

        address = await _addressRepository.CreateAsync(address);
        _logger.LogInformation("Создан адрес доставки {AddressId} для пользователя {UserId}", address.Id, userId);

        return MapToDto(address);
    }

    public async Task<DeliveryAddressDto> UpdateAddressAsync(Guid userId, Guid addressId, UpdateDeliveryAddressDto dto)
    {
        var address = await _addressRepository.GetByIdAsync(addressId);
        if (address == null || address.UserAccountId != userId)
            throw new InvalidOperationException("Адрес не найден");

        address.Address = dto.Address;
        address.City = dto.City;
        address.Region = dto.Region;
        address.PostalCode = dto.PostalCode;
        address.Apartment = dto.Apartment;
        address.IsDefault = dto.IsDefault;

        address = await _addressRepository.UpdateAsync(address);
        _logger.LogInformation("Обновлен адрес доставки {AddressId} для пользователя {UserId}", addressId, userId);

        return MapToDto(address);
    }

    public async Task<bool> DeleteAddressAsync(Guid userId, Guid addressId)
    {
        var address = await _addressRepository.GetByIdAsync(addressId);
        if (address == null || address.UserAccountId != userId)
            return false;

        var result = await _addressRepository.DeleteAsync(addressId);
        if (result)
        {
            _logger.LogInformation("Удален адрес доставки {AddressId} для пользователя {UserId}", addressId, userId);
        }

        return result;
    }

    public async Task<DeliveryAddressDto> SetDefaultAddressAsync(Guid userId, Guid addressId)
    {
        var address = await _addressRepository.GetByIdAsync(addressId);
        if (address == null || address.UserAccountId != userId)
            throw new InvalidOperationException("Адрес не найден");

        await _addressRepository.SetDefaultAsync(userId, addressId);
        address = await _addressRepository.GetByIdAsync(addressId);

        _logger.LogInformation("Установлен адрес по умолчанию {AddressId} для пользователя {UserId}", addressId, userId);

        return MapToDto(address!);
    }

    private static DeliveryAddressDto MapToDto(DeliveryAddress address)
    {
        return new DeliveryAddressDto
        {
            Id = address.Id,
            Address = address.Address,
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

