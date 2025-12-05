namespace InternetShopService_back.Modules.OrderManagement.Services;

public interface IPdfGenerationService
{
    Task<byte[]> GenerateInvoicePdfAsync(Guid invoiceId);
    Task<byte[]> GenerateUpdPdfAsync(Guid updId);
}

