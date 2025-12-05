namespace InternetShopService_back.Modules.UserCabinet.DTOs;

public class AuthResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public bool RequiresPasswordSetup { get; set; }
    public UserInfoDto? User { get; set; }
}

public class UserInfoDto
{
    public Guid Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public Guid CounterpartyId { get; set; }
}

