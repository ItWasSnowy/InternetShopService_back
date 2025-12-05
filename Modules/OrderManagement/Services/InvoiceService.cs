using InternetShopService_back.Modules.OrderManagement.DTOs;

namespace InternetShopService_back.Modules.OrderManagement.Services;

public class InvoiceService : IInvoiceService
{
    public Task<InvoiceDto> CreateInvoiceAsync(CreateInvoiceDto dto)
    {
        throw new NotImplementedException();
    }

    public Task<InvoiceDto> GetInvoiceAsync(Guid invoiceId)
    {
        throw new NotImplementedException();
    }

    public Task<UpdDocumentDto> CreateUpdFromInvoiceAsync(Guid invoiceId)
    {
        throw new NotImplementedException();
    }

    public Task<byte[]> GenerateInvoicePdfAsync(Guid invoiceId)
    {
        throw new NotImplementedException();
    }

    public Task<byte[]> GenerateUpdPdfAsync(Guid updId)
    {
        throw new NotImplementedException();
    }
}

