namespace InternetShopService_back.Shared.Models;

/// <summary>
/// Интернет-магазин - сервис, которым владеет компания в FimBiz
/// </summary>
public class Shop
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Название интернет-магазина
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Домен интернет-магазина (опционально, для идентификации по subdomain)
    /// </summary>
    public string? Domain { get; set; }
    
    /// <summary>
    /// ID компании в FimBiz (обязательно) - владелец магазина
    /// </summary>
    public int FimBizCompanyId { get; set; }
    
    /// <summary>
    /// ID организации в FimBiz (опционально)
    /// </summary>
    public int? FimBizOrganizationId { get; set; }

    public string? FimBizGrpcEndpoint { get; set; }

    public string? FimBizApiKey { get; set; }
    
    /// <summary>
    /// Активен ли магазин
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Navigation properties
    public virtual ICollection<InternetShopService_back.Modules.UserCabinet.Models.UserAccount> UserAccounts { get; set; } = new List<InternetShopService_back.Modules.UserCabinet.Models.UserAccount>();
}

