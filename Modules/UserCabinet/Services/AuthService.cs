using InternetShopService_back.Infrastructure.Calls;
using InternetShopService_back.Infrastructure.Grpc;
using InternetShopService_back.Infrastructure.Jwt;
using InternetShopService_back.Modules.UserCabinet.DTOs;
using InternetShopService_back.Modules.UserCabinet.Models;
using InternetShopService_back.Modules.UserCabinet.Repositories;
using InternetShopService_back.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using BCrypt.Net;

namespace InternetShopService_back.Modules.UserCabinet.Services;

public class AuthService : IAuthService
{
    private readonly IUserAccountRepository _userAccountRepository;
    private readonly ISessionRepository _sessionRepository;
    private readonly ICallService _callService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IFimBizGrpcClient _fimBizGrpcClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    private readonly int _maxLoginAttempts;
    private readonly int _lockoutHours;
    private readonly int _codeExpirationMinutes;

    public AuthService(
        IUserAccountRepository userAccountRepository,
        ISessionRepository sessionRepository,
        ICallService callService,
        IJwtTokenService jwtTokenService,
        IFimBizGrpcClient fimBizGrpcClient,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _userAccountRepository = userAccountRepository;
        _sessionRepository = sessionRepository;
        _callService = callService;
        _jwtTokenService = jwtTokenService;
        _fimBizGrpcClient = fimBizGrpcClient;
        _configuration = configuration;
        _logger = logger;

        _maxLoginAttempts = _configuration.GetValue<int>("AuthSettings:MaxLoginAttempts", 3);
        _lockoutHours = _configuration.GetValue<int>("AuthSettings:LockoutHours", 3);
        _codeExpirationMinutes = _configuration.GetValue<int>("AuthSettings:CodeExpirationMinutes", 30);
    }

    public async Task<AuthResponseDto> RequestPhoneCodeAsync(string phoneNumber)
    {
        try
        {
            // Проверка формата номера
            if (!IsValidPhoneNumber(phoneNumber))
            {
                throw new ArgumentException("Номер телефона должен быть в формате 7XXXXXXXXXX");
            }

            // Поиск или создание пользователя
            var userAccount = await _userAccountRepository.GetByPhoneNumberAsync(phoneNumber);
            
            if (userAccount == null)
            {
                // Проверяем существование контрагента через FimBiz
                var counterparty = await _fimBizGrpcClient.GetCounterpartyAsync(phoneNumber);
                if (counterparty == null)
                {
                    throw new InvalidOperationException("Контрагент с таким номером телефона не найден");
                }

                // Создаем новый аккаунт
                userAccount = new UserAccount
                {
                    Id = Guid.NewGuid(),
                    CounterpartyId = counterparty.Id,
                    PhoneNumber = phoneNumber,
                    IsFirstLogin = true,
                    IsPasswordSet = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _userAccountRepository.CreateAsync(userAccount);
            }

            // Проверка лимита попыток входа
            if (userAccount.AccessFailedCount >= _maxLoginAttempts)
            {
                if (userAccount.FirstFailedLoginAttempt.HasValue &&
                    DateTime.UtcNow - userAccount.FirstFailedLoginAttempt.Value <= TimeSpan.FromHours(_lockoutHours))
                {
                    var remainingTime = userAccount.FirstFailedLoginAttempt.Value.AddHours(_lockoutHours) - DateTime.UtcNow;
                    throw new InvalidOperationException(
                        $"Вы превысили максимальное количество неудачных попыток входа. " +
                        $"До следующей возможности попытки осталось {remainingTime.Hours} часов {remainingTime.Minutes} минут");
                }
                else
                {
                    // Сброс счетчика после истечения времени блокировки
                    userAccount.AccessFailedCount = 0;
                    userAccount.FirstFailedLoginAttempt = null;
                }
            }

            // Отправка звонка
            var callRequest = new CallRequestDto { PhoneNumber = phoneNumber };
            var callResult = await _callService.SendCallAndUpdateUserAsync(callRequest, userAccount);

            if (!callResult.Success)
            {
                if (callResult.IsCallLimitExceeded)
                {
                    throw new InvalidOperationException(
                        $"Заявки на звонок были исчерпаны. Попробуйте ещё раз через {callResult.RemainingWaitTimeMinutes} минут");
                }
                throw new InvalidOperationException(callResult.Message);
            }

            if (string.IsNullOrEmpty(callResult.LastFourDigits))
            {
                throw new InvalidOperationException("Не удалось получить код подтверждения");
            }

            // Сохранение обновленного пользователя
            await _userAccountRepository.UpdateAsync(userAccount);

            _logger.LogInformation("Код подтверждения отправлен на номер {PhoneNumber}", phoneNumber);

            return new AuthResponseDto
            {
                AccessToken = string.Empty,
                RefreshToken = string.Empty,
                RequiresPasswordSetup = false,
                User = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при запросе кода на номер {PhoneNumber}", phoneNumber);
            throw;
        }
    }

    public async Task<AuthResponseDto> VerifyCodeAsync(string phoneNumber, string code)
    {
        try
        {
            var userAccount = await _userAccountRepository.GetByPhoneNumberAsync(phoneNumber);
            if (userAccount == null)
            {
                throw new InvalidOperationException("Пользователь не найден");
            }

            // Проверка кода
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(userAccount.PhoneCallDigits))
            {
                await HandleFailedLogin(userAccount);
                throw new UnauthorizedAccessException("Неверный код подтверждения");
            }

            // Проверка времени действия кода (30 минут)
            if (!userAccount.PhoneCallDateTime.HasValue ||
                (DateTime.UtcNow - userAccount.PhoneCallDateTime.Value).TotalMinutes > _codeExpirationMinutes)
            {
                await HandleFailedLogin(userAccount);
                throw new UnauthorizedAccessException("Истекло время действия кода. Запросите новый звонок");
            }

            // Сравнение кода
            if (userAccount.PhoneCallDigits != code)
            {
                await HandleFailedLogin(userAccount);
                throw new UnauthorizedAccessException("Неверный код подтверждения");
            }

            // Очистка кода после успешной проверки
            userAccount.PhoneCallDigits = null;
            userAccount.PhoneCallDateTime = null;
            userAccount.AccessFailedCount = 0;
            userAccount.FirstFailedLoginAttempt = null;
            userAccount.LastLoginAt = DateTime.UtcNow;

            // Проверка, нужно ли запросить установку пароля
            bool requiresPasswordSetup = userAccount.IsFirstLogin && !userAccount.IsPasswordSet;
            if (userAccount.IsFirstLogin)
            {
                userAccount.IsFirstLogin = false;
            }

            // Деактивация старых сессий (опционально, можно сделать настройку)
            await _userAccountRepository.DeactivateSessionsAsync(userAccount.Id);

            // Генерация токенов
            var (accessToken, refreshToken) = _jwtTokenService.GenerateTokens(
                userAccount.Id,
                userAccount.PhoneNumber,
                userAccount.CounterpartyId);

            // Создание сессии
            var session = new Session
            {
                Id = Guid.NewGuid(),
                UserAccountId = userAccount.Id,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = DateTime.UtcNow.AddHours(24), // Refresh token живет 24 часа
                IsActive = true
            };

            await _sessionRepository.CreateAsync(session);
            await _userAccountRepository.UpdateAsync(userAccount);

            _logger.LogInformation("Пользователь {UserId} успешно авторизован по коду", userAccount.Id);

            return new AuthResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                RequiresPasswordSetup = requiresPasswordSetup,
                User = new UserInfoDto
                {
                    Id = userAccount.Id,
                    PhoneNumber = userAccount.PhoneNumber,
                    CounterpartyId = userAccount.CounterpartyId
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при проверке кода для номера {PhoneNumber}", phoneNumber);
            throw;
        }
    }

    public async Task<AuthResponseDto> SetPasswordAsync(string phoneNumber, string password)
    {
        try
        {
            var userAccount = await _userAccountRepository.GetByPhoneNumberAsync(phoneNumber);
            if (userAccount == null)
            {
                throw new InvalidOperationException("Пользователь не найден");
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            {
                throw new ArgumentException("Пароль должен содержать минимум 6 символов");
            }

            // Хеширование пароля
            userAccount.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            userAccount.IsPasswordSet = true;
            userAccount.UpdatedAt = DateTime.UtcNow;

            await _userAccountRepository.UpdateAsync(userAccount);

            _logger.LogInformation("Пароль установлен для пользователя {UserId}", userAccount.Id);

            // Генерация новых токенов после установки пароля
            var (accessToken, refreshToken) = _jwtTokenService.GenerateTokens(
                userAccount.Id,
                userAccount.PhoneNumber,
                userAccount.CounterpartyId);

            var session = new Session
            {
                Id = Guid.NewGuid(),
                UserAccountId = userAccount.Id,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                IsActive = true
            };

            await _sessionRepository.CreateAsync(session);

            return new AuthResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                RequiresPasswordSetup = false,
                User = new UserInfoDto
                {
                    Id = userAccount.Id,
                    PhoneNumber = userAccount.PhoneNumber,
                    CounterpartyId = userAccount.CounterpartyId
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при установке пароля для номера {PhoneNumber}", phoneNumber);
            throw;
        }
    }

    public async Task<AuthResponseDto> LoginByPasswordAsync(string phoneNumber, string password)
    {
        try
        {
            var userAccount = await _userAccountRepository.GetByPhoneNumberAsync(phoneNumber);
            if (userAccount == null)
            {
                throw new UnauthorizedAccessException("Неверный номер телефона или пароль");
            }

            if (!userAccount.IsPasswordSet || string.IsNullOrEmpty(userAccount.PasswordHash))
            {
                throw new InvalidOperationException("Пароль не установлен. Используйте авторизацию по звонку");
            }

            // Проверка лимита попыток
            if (userAccount.AccessFailedCount >= _maxLoginAttempts)
            {
                if (userAccount.FirstFailedLoginAttempt.HasValue &&
                    DateTime.UtcNow - userAccount.FirstFailedLoginAttempt.Value <= TimeSpan.FromHours(_lockoutHours))
                {
                    var remainingTime = userAccount.FirstFailedLoginAttempt.Value.AddHours(_lockoutHours) - DateTime.UtcNow;
                    throw new InvalidOperationException(
                        $"Вы превысили максимальное количество неудачных попыток входа. " +
                        $"До следующей возможности попытки осталось {remainingTime.Hours} часов {remainingTime.Minutes} минут");
                }
                else
                {
                    userAccount.AccessFailedCount = 0;
                    userAccount.FirstFailedLoginAttempt = null;
                }
            }

            // Проверка пароля
            if (!BCrypt.Net.BCrypt.Verify(password, userAccount.PasswordHash))
            {
                await HandleFailedLogin(userAccount);
                throw new UnauthorizedAccessException("Неверный номер телефона или пароль");
            }

            // Успешный вход
            userAccount.AccessFailedCount = 0;
            userAccount.FirstFailedLoginAttempt = null;
            userAccount.LastLoginAt = DateTime.UtcNow;

            // Деактивация старых сессий
            await _userAccountRepository.DeactivateSessionsAsync(userAccount.Id);

            // Генерация токенов
            var (accessToken, refreshToken) = _jwtTokenService.GenerateTokens(
                userAccount.Id,
                userAccount.PhoneNumber,
                userAccount.CounterpartyId);

            var session = new Session
            {
                Id = Guid.NewGuid(),
                UserAccountId = userAccount.Id,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                IsActive = true
            };

            await _sessionRepository.CreateAsync(session);
            await _userAccountRepository.UpdateAsync(userAccount);

            _logger.LogInformation("Пользователь {UserId} успешно авторизован по паролю", userAccount.Id);

            return new AuthResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                RequiresPasswordSetup = false,
                User = new UserInfoDto
                {
                    Id = userAccount.Id,
                    PhoneNumber = userAccount.PhoneNumber,
                    CounterpartyId = userAccount.CounterpartyId
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при входе по паролю для номера {PhoneNumber}", phoneNumber);
            throw;
        }
    }

    public async Task<bool> ValidateTokenAsync(string token)
    {
        try
        {
            var principal = _jwtTokenService.ValidateAccessToken(token);
            if (principal == null)
                return false;

            // Проверка существования сессии в БД
            var session = await _sessionRepository.GetByAccessTokenAsync(token);
            return session != null && session.IsActive && session.ExpiresAt > DateTime.UtcNow;
        }
        catch
        {
            return false;
        }
    }

    public async Task LogoutAsync(string token)
    {
        try
        {
            var session = await _sessionRepository.GetByAccessTokenAsync(token);
            if (session != null)
            {
                session.IsActive = false;
                await _sessionRepository.UpdateAsync(session);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при выходе пользователя");
            throw;
        }
    }

    private async Task HandleFailedLogin(UserAccount userAccount)
    {
        userAccount.AccessFailedCount++;
        if (userAccount.FirstFailedLoginAttempt == null)
        {
            userAccount.FirstFailedLoginAttempt = DateTime.UtcNow;
        }
        await _userAccountRepository.UpdateAsync(userAccount);
    }

    private bool IsValidPhoneNumber(string phoneNumber)
    {
        return !string.IsNullOrEmpty(phoneNumber) &&
               System.Text.RegularExpressions.Regex.IsMatch(phoneNumber, @"^7\d{10}$");
    }
}
