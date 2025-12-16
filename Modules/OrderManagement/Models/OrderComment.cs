namespace InternetShopService_back.Modules.OrderManagement.Models;

public class OrderComment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string ExternalCommentId { get; set; } = string.Empty; // GUID из FimBiz или интернет-магазина
    public string CommentText { get; set; } = string.Empty;
    public int? AuthorProfileId { get; set; } // ID профиля автора в FimBiz
    public Guid? AuthorUserId { get; set; } // ID пользователя автора (для комментариев из интернет-магазина)
    public string? AuthorName { get; set; } // Имя автора (для комментариев из интернет-магазина)
    public bool IsFromInternetShop { get; set; } // Флаг: создан ли в интернет-магазине
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Navigation properties
    public virtual Order Order { get; set; } = null!;
    public virtual ICollection<OrderCommentAttachment> Attachments { get; set; } = new List<OrderCommentAttachment>();
}

public class OrderCommentAttachment
{
    public Guid Id { get; set; }
    public Guid OrderCommentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty; // Абсолютный URL файла
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public virtual OrderComment OrderComment { get; set; } = null!;
}

