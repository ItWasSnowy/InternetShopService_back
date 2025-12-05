using InternetShopService_back.Infrastructure.Calls;
using InternetShopService_back.Infrastructure.Grpc;
using InternetShopService_back.Infrastructure.Jwt;
using InternetShopService_back.Modules.UserCabinet.DTOs;
using InternetShopService_back.Modules.UserCabinet.Models;
using InternetShopService_back.Modules.UserCabinet.Repositories;
using InternetShopService_back.Shared.Models;
using InternetShopService_back.Shared.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using BCrypt.Net;

namespace InternetShopService_back.Modules.UserCabinet.Services;

public class AuthService : IAuthService
{
    private readonly IUserAccountRepository _userAccountRepository;
    private readonly ISessionRepository _sessionRepository;
    private readonly ICounterpartyRepository _counterpartyRepository;
    private readonly IShopRepository _shopRepository;
    private readonly ICallService _callService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IFimBizGrpcClient _fimBizGrpcClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    private readonly int _maxLoginAttempts;
    private readonly int _lockoutHours;
    private readonly int _codeExpirationMinutes;

    public AuthService(
        IUserAccountRepository userAccountRepository,
        ISessionRepository sessionRepository,
        ICounterpartyRepository counterpartyRepository,
        IShopRepository shopRepository,
        ICallService callService,
        IJwtTokenService jwtTokenService,
        IFimBizGrpcClient fimBizGrpcClient,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _userAccountRepository = userAccountRepository;
        _sessionRepository = sessionRepository;
        _counterpartyRepository = counterpartyRepository;
        _shopRepository = shopRepository;
        _callService = callService;
        _jwtTokenService = jwtTokenService;
        _fimBizGrpcClient = fimBizGrpcClient;
        _httpContextAccessor = httpContextAccessor;
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
                var fimBizCounterparty = await _fimBizGrpcClient.GetCounterpartyAsync(phoneNumber);
                if (fimBizCounterparty == null)
                {
                    throw new InvalidOperationException("Контрагент с таким номером телефона не найден в FimBiz");
                }

                // Проверяем флаг создания кабинета
                if (!fimBizCounterparty.IsCreateCabinet)
                {
                    throw new InvalidOperationException("Для данного контрагента не разрешено создание кабинета в интернет-магазине");
                }

                // Проверяем, существует ли контрагент в локальной БД
                Counterparty? localCounterparty = null;
                
                if (fimBizCounterparty.FimBizContractorId.HasValue)
                {
                    // Ищем по FimBizContractorId
                    localCounterparty = await _counterpartyRepository.GetByFimBizIdAsync(fimBizCounterparty.FimBizContractorId.Value);
                }
                
                if (localCounterparty == null)
                {
                    // Ищем по номеру телефона
                    localCounterparty = await _counterpartyRepository.GetByPhoneNumberAsync(phoneNumber);
                }

                // Если контрагента нет в БД, сохраняем его
                if (localCounterparty == null)
                {
                    localCounterparty = fimBizCounterparty;
                    localCounterparty = await _counterpartyRepository.CreateAsync(localCounterparty);
                    _logger.LogInformation("Создан новый контрагент {CounterpartyId} для номера {PhoneNumber}", 
                        localCounterparty.Id, phoneNumber);
                }
                else
                {
                    // Обновляем данные контрагента из FimBiz
                    localCounterparty.Name = fimBizCounterparty.Name;
                    localCounterparty.PhoneNumber = fimBizCounterparty.PhoneNumber;
                    localCounterparty.Type = fimBizCounterparty.Type;
                    localCounterparty.Email = fimBizCounterparty.Email;
                    localCounterparty.Inn = fimBizCounterparty.Inn;
                    localCounterparty.Kpp = fimBizCounterparty.Kpp;
                    localCounterparty.LegalAddress = fimBizCounterparty.LegalAddress;
                    localCounterparty.EdoIdentifier = fimBizCounterparty.EdoIdentifier;
                    localCounterparty.HasPostPayment = fimBizCounterparty.HasPostPayment;
                    localCounterparty.FimBizContractorId = fimBizCounterparty.FimBizContractorId;
                    localCounterparty.FimBizCompanyId = fimBizCounterparty.FimBizCompanyId;
                    localCounterparty.FimBizOrganizationId = fimBizCounterparty.FimBizOrganizationId;
                    localCounterparty.LastSyncVersion = fimBizCounterparty.LastSyncVersion;
                    localCounterparty.IsCreateCabinet = fimBizCounterparty.IsCreateCabinet;
                    localCounterparty.UpdatedAt = DateTime.UtcNow;
                    
                    localCounterparty = await _counterpartyRepository.UpdateAsync(localCounterparty);
                }

                // Находим Shop по FimBizCompanyId контрагента
                if (!localCounterparty.FimBizCompanyId.HasValue)
                {
                    throw new InvalidOperationException("У контрагента не указан FimBizCompanyId. Невозможно определить магазин.");
                }

                var shop = await _shopRepository.GetByFimBizCompanyIdAsync(
                    localCounterparty.FimBizCompanyId.Value,
                    localCounterparty.FimBizOrganizationId);

                if (shop == null || !shop.IsActive)
                {
                    throw new InvalidOperationException(
                        $"Интернет-магазин для компании {localCounterparty.FimBizCompanyId} не найден или неактивен. " +
                        $"Обратитесь к администратору для создания магазина.");
                }

                // Создаем новый аккаунт с правильным CounterpartyId и ShopId
                userAccount = new UserAccount
                {
                    Id = Guid.NewGuid(),
                    CounterpartyId = localCounterparty.Id,
                    ShopId = shop.Id,
                    PhoneNumber = phoneNumber,
                    IsFirstLogin = true,
                    IsPasswordSet = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _userAccountRepository.CreateAsync(userAccount);
                _logger.LogInformation("Создан новый аккаунт пользователя {UserId} для контрагента {CounterpartyId}", 
                    userAccount.Id, localCounterparty.Id);
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
            // Запрашиваем установку пароля, если он не установлен (независимо от первого входа)
            bool requiresPasswordSetup = !userAccount.IsPasswordSet;
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
                userAccount.CounterpartyId,
                userAccount.ShopId);

            // Создание сессии с информацией об устройстве
            var deviceInfo = GetDeviceInfo();
            var session = new Session
            {
                Id = Guid.NewGuid(),
                UserAccountId = userAccount.Id,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = DateTime.UtcNow.AddHours(24), // Refresh token живет 24 часа
                IsActive = true,
                DeviceInfo = deviceInfo.deviceInfo,
                UserAgent = deviceInfo.userAgent,
                IpAddress = deviceInfo.ipAddress
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
                userAccount.CounterpartyId,
                userAccount.ShopId);

            var deviceInfo = GetDeviceInfo();
            var session = new Session
            {
                Id = Guid.NewGuid(),
                UserAccountId = userAccount.Id,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                IsActive = true,
                DeviceInfo = deviceInfo.deviceInfo,
                UserAgent = deviceInfo.userAgent,
                IpAddress = deviceInfo.ipAddress
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
                userAccount.CounterpartyId,
                userAccount.ShopId);

            var deviceInfo = GetDeviceInfo();
            var session = new Session
            {
                Id = Guid.NewGuid(),
                UserAccountId = userAccount.Id,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                IsActive = true,
                DeviceInfo = deviceInfo.deviceInfo,
                UserAgent = deviceInfo.userAgent,
                IpAddress = deviceInfo.ipAddress
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

    public async Task<AuthResponseDto> RefreshTokenAsync(string refreshToken)
    {
        try
        {
            if (string.IsNullOrEmpty(refreshToken))
            {
                throw new ArgumentException("Refresh token не предоставлен");
            }

            // Находим сессию по refresh token
            var session = await _sessionRepository.GetByRefreshTokenAsync(refreshToken);
            if (session == null)
            {
                throw new UnauthorizedAccessException("Неверный refresh token");
            }

            // Проверяем, что сессия активна и не истекла
            if (!session.IsActive)
            {
                throw new UnauthorizedAccessException("Сессия деактивирована");
            }

            if (session.ExpiresAt <= DateTime.UtcNow)
            {
                // Деактивируем истекшую сессию
                session.IsActive = false;
                await _sessionRepository.UpdateAsync(session);
                throw new UnauthorizedAccessException("Refresh token истек. Необходима повторная авторизация");
            }

            // Получаем данные пользователя
            var userAccount = await _userAccountRepository.GetByIdAsync(session.UserAccountId);
            if (userAccount == null)
            {
                throw new InvalidOperationException("Пользователь не найден");
            }

            // Генерируем новые токены
            var (newAccessToken, newRefreshToken) = _jwtTokenService.GenerateTokens(
                userAccount.Id,
                userAccount.PhoneNumber,
                userAccount.CounterpartyId,
                userAccount.ShopId);

            // Обновляем сессию с новыми токенами
            session.AccessToken = newAccessToken;
            session.RefreshToken = newRefreshToken;
            session.ExpiresAt = DateTime.UtcNow.AddHours(24); // Refresh token живет 24 часа
            await _sessionRepository.UpdateAsync(session);

            _logger.LogInformation("Токены обновлены для пользователя {UserId}", userAccount.Id);

            return new AuthResponseDto
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken,
                RequiresPasswordSetup = false,
                User = new UserInfoDto
                {
                    Id = userAccount.Id,
                    PhoneNumber = userAccount.PhoneNumber,
                    CounterpartyId = userAccount.CounterpartyId
                }
            };
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Ошибка валидации refresh token");
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Неверный или истекший refresh token");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обновлении токена");
            throw;
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

    private (string? deviceInfo, string? userAgent, string? ipAddress) GetDeviceInfo()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return (null, null, null);

        var userAgent = httpContext.Request.Headers["User-Agent"].ToString();
        var ipAddress = GetClientIpAddress(httpContext);
        var deviceInfo = ParseDeviceInfo(userAgent);

        return (deviceInfo, userAgent, ipAddress);
    }

    private string? ParseDeviceInfo(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
            return null;

        // Парсим User-Agent для определения браузера и ОС
        var browser = "Unknown";
        var os = "Unknown";

        // Определяем браузер
        if (userAgent.Contains("Chrome", StringComparison.OrdinalIgnoreCase) && !userAgent.Contains("Edg", StringComparison.OrdinalIgnoreCase))
            browser = "Chrome";
        else if (userAgent.Contains("Firefox", StringComparison.OrdinalIgnoreCase))
            browser = "Firefox";
        else if (userAgent.Contains("Safari", StringComparison.OrdinalIgnoreCase) && !userAgent.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
            browser = "Safari";
        else if (userAgent.Contains("Edg", StringComparison.OrdinalIgnoreCase))
            browser = "Edge";
        else if (userAgent.Contains("Opera", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("OPR", StringComparison.OrdinalIgnoreCase))
            browser = "Opera";

        // Определяем ОС
        if (userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase))
            os = "Windows";
        else if (userAgent.Contains("Mac OS", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("MacOS", StringComparison.OrdinalIgnoreCase))
            os = "macOS";
        else if (userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase))
            os = "Linux";
        else if (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase))
            os = "Android";
        else if (userAgent.Contains("iOS", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase))
            os = "iOS";

        return $"{browser} on {os}";
    }

    private string? GetClientIpAddress(HttpContext httpContext)
    {
        // Проверяем заголовки прокси
        var ipAddress = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(ipAddress))
        {
            // X-Forwarded-For может содержать несколько IP через запятую
            var ips = ipAddress.Split(',');
            if (ips.Length > 0)
                return ips[0].Trim();
        }

        ipAddress = httpContext.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(ipAddress))
            return ipAddress;

        // Используем RemoteIpAddress как fallback
        return httpContext.Connection.RemoteIpAddress?.ToString();
    }
}
