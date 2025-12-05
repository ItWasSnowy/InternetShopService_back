using InternetShopService_back.Shared.Models;

namespace InternetShopService_back.Modules.OrderManagement.Models;

public class UpdDocument
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid CounterpartyId { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public DateTime DocumentDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public virtual Order Order { get; set; } = null!;
    public virtual Invoice Invoice { get; set; } = null!;
    public virtual Counterparty Counterparty { get; set; } = null!;
}

