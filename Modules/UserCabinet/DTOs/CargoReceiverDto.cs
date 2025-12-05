namespace InternetShopService_back.Modules.UserCabinet.DTOs;

public class CargoReceiverDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PassportSeries { get; set; } = string.Empty;
    public string PassportNumber { get; set; } = string.Empty;
    public string? PassportIssuedBy { get; set; }
    public DateTime? PassportIssueDate { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateCargoReceiverDto
{
    public string FullName { get; set; } = string.Empty;
    public string PassportSeries { get; set; } = string.Empty;
    public string PassportNumber { get; set; } = string.Empty;
    public string? PassportIssuedBy { get; set; }
    public DateTime? PassportIssueDate { get; set; }
    public bool IsDefault { get; set; }
}

public class UpdateCargoReceiverDto
{
    public string FullName { get; set; } = string.Empty;
    public string PassportSeries { get; set; } = string.Empty;
    public string PassportNumber { get; set; } = string.Empty;
    public string? PassportIssuedBy { get; set; }
    public DateTime? PassportIssueDate { get; set; }
    public bool IsDefault { get; set; }
}

