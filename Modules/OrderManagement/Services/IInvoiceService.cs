using InternetShopService_back.Modules.OrderManagement.DTOs;

namespace InternetShopService_back.Modules.OrderManagement.Services;

public interface IInvoiceService
{
    Task<InvoiceDto> CreateInvoiceAsync(CreateInvoiceDto dto);
    Task<InvoiceDto> GetInvoiceAsync(Guid invoiceId);
    Task<UpdDocumentDto> CreateUpdFromInvoiceAsync(Guid invoiceId);
    Task<byte[]> GenerateInvoicePdfAsync(Guid invoiceId);
    Task<byte[]> GenerateUpdPdfAsync(Guid updId);
}

