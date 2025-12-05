namespace InternetShopService_back.Modules.OrderManagement.Services;

public class PdfGenerationService : IPdfGenerationService
{
    public Task<byte[]> GenerateInvoicePdfAsync(Guid invoiceId)
    {
        throw new NotImplementedException();
    }

    public Task<byte[]> GenerateUpdPdfAsync(Guid updId)
    {
        throw new NotImplementedException();
    }
}

