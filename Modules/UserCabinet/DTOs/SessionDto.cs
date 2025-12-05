namespace InternetShopService_back.Modules.UserCabinet.DTOs;

public class SessionDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public string? DeviceInfo { get; set; }
    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }
    public string? DeviceName { get; set; }
    public bool IsCurrentSession { get; set; }
}

public class DeactivateSessionRequestDto
{
    public Guid SessionId { get; set; }
}

public class DeactivateSessionsRequestDto
{
    public List<Guid> SessionIds { get; set; } = new();
}
