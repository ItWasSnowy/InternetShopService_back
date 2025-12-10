using InternetShopService_back.Shared.Models;

namespace InternetShopService_back.Modules.OrderManagement.Models;

public class Invoice
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid CounterpartyId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public decimal TotalAmount { get; set; }
    public bool IsConfirmed { get; set; }
    public bool IsPaid { get; set; }
    public int? FimBizBillId { get; set; } // ID счета в FimBiz
    public string? PdfUrl { get; set; } // URL для скачивания PDF счета
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public virtual Order Order { get; set; } = null!;
    public virtual Counterparty Counterparty { get; set; } = null!;
    public virtual ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
}

