using InternetShopService_back.Modules.UserCabinet.Models;

namespace InternetShopService_back.Modules.UserCabinet.Repositories;

public interface IUserAccountRepository
{
    Task<UserAccount?> GetByPhoneNumberAsync(string phoneNumber);
    Task<UserAccount?> GetByIdAsync(Guid id);
    Task<UserAccount> CreateAsync(UserAccount userAccount);
    Task<UserAccount> UpdateAsync(UserAccount userAccount);
    Task<List<Session>> GetActiveSessionsAsync(Guid userId);
    Task DeactivateSessionsAsync(Guid userId, Guid? excludeSessionId = null);
    Task<bool> ExistsByPhoneNumberAsync(string phoneNumber);
}

