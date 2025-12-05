namespace InternetShopService_back.Modules.UserCabinet.Models;

public class CargoReceiver
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PassportSeries { get; set; } = string.Empty;
    public string PassportNumber { get; set; } = string.Empty;
    public string? PassportIssuedBy { get; set; }
    public DateTime? PassportIssueDate { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public virtual UserAccount UserAccount { get; set; } = null!;
}

