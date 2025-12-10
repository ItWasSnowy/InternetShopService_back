namespace InternetShopService_back.Modules.OrderManagement.DTOs;

public class InvoiceDto
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public decimal TotalAmount { get; set; }
    public bool IsConfirmed { get; set; }
    public bool IsPaid { get; set; }
    public int? FimBizBillId { get; set; }
    public string? PdfUrl { get; set; }
    public List<InvoiceItemDto> Items { get; set; } = new();
}

public class InvoiceItemDto
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal TotalAmount { get; set; }
}

public class CreateInvoiceDto
{
    public Guid OrderId { get; set; }
    public Guid CounterpartyId { get; set; }
    public string? InvoiceNumber { get; set; }
    public List<CreateInvoiceItemDto> Items { get; set; } = new();
}

public class CreateInvoiceItemDto
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Unit { get; set; } = "шт";
    public decimal Price { get; set; }
}

public class UpdDocumentDto
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid InvoiceId { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public DateTime DocumentDate { get; set; }
}

