using System.Collections.Generic;

namespace InternetShopService_back.Modules.Notifications.DTOs;

public class PagedNotificationsDto
{
    public List<ShopNotificationDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
