using InternetShopService_back.Modules.UserCabinet.DTOs;
using InternetShopService_back.Modules.UserCabinet.Models;
using InternetShopService_back.Modules.UserCabinet.Repositories;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Modules.UserCabinet.Services;

public class CargoReceiverService : ICargoReceiverService
{
    private readonly ICargoReceiverRepository _receiverRepository;
    private readonly IUserAccountRepository _userAccountRepository;
    private readonly ILogger<CargoReceiverService> _logger;

    public CargoReceiverService(
        ICargoReceiverRepository receiverRepository,
        IUserAccountRepository userAccountRepository,
        ILogger<CargoReceiverService> logger)
    {
        _receiverRepository = receiverRepository;
        _userAccountRepository = userAccountRepository;
        _logger = logger;
    }

    public async Task<List<CargoReceiverDto>> GetReceiversAsync(Guid userId)
    {
        var receivers = await _receiverRepository.GetByUserIdAsync(userId);
        return receivers.Select(MapToDto).ToList();
    }

    public async Task<CargoReceiverDto?> GetReceiverAsync(Guid userId, Guid receiverId)
    {
        var receiver = await _receiverRepository.GetByIdAsync(receiverId);
        if (receiver == null || receiver.UserAccountId != userId)
            return null;

        return MapToDto(receiver);
    }

    public async Task<CargoReceiverDto?> GetDefaultReceiverAsync(Guid userId)
    {
        var receiver = await _receiverRepository.GetDefaultByUserIdAsync(userId);
        return receiver == null ? null : MapToDto(receiver);
    }

    public async Task<CargoReceiverDto> CreateReceiverAsync(Guid userId, CreateCargoReceiverDto dto)
    {
        var userAccount = await _userAccountRepository.GetByIdAsync(userId);
        if (userAccount == null)
            throw new InvalidOperationException("Пользователь не найден");

        var receiver = new CargoReceiver
        {
            Id = Guid.NewGuid(),
            UserAccountId = userId,
            FullName = dto.FullName,
            PassportSeries = dto.PassportSeries,
            PassportNumber = dto.PassportNumber,
            PassportIssuedBy = dto.PassportIssuedBy,
            PassportIssueDate = dto.PassportIssueDate,
            IsDefault = dto.IsDefault
        };

        receiver = await _receiverRepository.CreateAsync(receiver);
        _logger.LogInformation("Создан грузополучатель {ReceiverId} для пользователя {UserId}", receiver.Id, userId);

        return MapToDto(receiver);
    }

    public async Task<CargoReceiverDto> UpdateReceiverAsync(Guid userId, Guid receiverId, UpdateCargoReceiverDto dto)
    {
        var receiver = await _receiverRepository.GetByIdAsync(receiverId);
        if (receiver == null || receiver.UserAccountId != userId)
            throw new InvalidOperationException("Грузополучатель не найден");

        receiver.FullName = dto.FullName;
        receiver.PassportSeries = dto.PassportSeries;
        receiver.PassportNumber = dto.PassportNumber;
        receiver.PassportIssuedBy = dto.PassportIssuedBy;
        receiver.PassportIssueDate = dto.PassportIssueDate;
        receiver.IsDefault = dto.IsDefault;

        receiver = await _receiverRepository.UpdateAsync(receiver);
        _logger.LogInformation("Обновлен грузополучатель {ReceiverId} для пользователя {UserId}", receiverId, userId);

        return MapToDto(receiver);
    }

    public async Task<bool> DeleteReceiverAsync(Guid userId, Guid receiverId)
    {
        var receiver = await _receiverRepository.GetByIdAsync(receiverId);
        if (receiver == null || receiver.UserAccountId != userId)
            return false;

        var result = await _receiverRepository.DeleteAsync(receiverId);
        if (result)
        {
            _logger.LogInformation("Удален грузополучатель {ReceiverId} для пользователя {UserId}", receiverId, userId);
        }

        return result;
    }

    public async Task<CargoReceiverDto> SetDefaultReceiverAsync(Guid userId, Guid receiverId)
    {
        var receiver = await _receiverRepository.GetByIdAsync(receiverId);
        if (receiver == null || receiver.UserAccountId != userId)
            throw new InvalidOperationException("Грузополучатель не найден");

        await _receiverRepository.SetDefaultAsync(userId, receiverId);
        receiver = await _receiverRepository.GetByIdAsync(receiverId);

        _logger.LogInformation("Установлен грузополучатель по умолчанию {ReceiverId} для пользователя {UserId}", receiverId, userId);

        return MapToDto(receiver!);
    }

    private static CargoReceiverDto MapToDto(CargoReceiver receiver)
    {
        return new CargoReceiverDto
        {
            Id = receiver.Id,
            FullName = receiver.FullName,
            PassportSeries = receiver.PassportSeries,
            PassportNumber = receiver.PassportNumber,
            PassportIssuedBy = receiver.PassportIssuedBy,
            PassportIssueDate = receiver.PassportIssueDate,
            IsDefault = receiver.IsDefault,
            CreatedAt = receiver.CreatedAt,
            UpdatedAt = receiver.UpdatedAt
        };
    }
}

