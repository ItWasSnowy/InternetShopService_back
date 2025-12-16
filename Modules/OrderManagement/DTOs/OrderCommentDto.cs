namespace InternetShopService_back.Modules.OrderManagement.DTOs;

public class OrderCommentDto
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string ExternalCommentId { get; set; } = string.Empty;
    public string CommentText { get; set; } = string.Empty;
    public int? AuthorProfileId { get; set; }
    public Guid? AuthorUserId { get; set; }
    public string? AuthorName { get; set; }
    public bool IsFromInternetShop { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<OrderCommentAttachmentDto> Attachments { get; set; } = new();
}

public class OrderCommentAttachmentDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class CreateOrderCommentDto
{
    public Guid OrderId { get; set; }
    public string CommentText { get; set; } = string.Empty;
    public string? AuthorName { get; set; }
    public List<CreateOrderCommentAttachmentDto> Attachments { get; set; } = new();
}

public class CreateOrderCommentAttachmentDto
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
}

public class UpdateOrderCommentDto
{
    public string CommentText { get; set; } = string.Empty;
}

