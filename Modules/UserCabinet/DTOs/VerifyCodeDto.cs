namespace InternetShopService_back.Modules.UserCabinet.DTOs;

public class VerifyCodeDto
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

