using System.ComponentModel.DataAnnotations;

namespace InternetShopService_back.Modules.OrderManagement.DTOs;

/// <summary>
/// DTO для запроса звонка для подтверждения счета
/// </summary>
public class RequestInvoiceConfirmationCodeDto
{
    // Номер телефона берется из авторизованного пользователя
}

/// <summary>
/// DTO для подтверждения счета по коду из звонка
/// </summary>
public class ConfirmInvoiceByPhoneDto
{
    [Required(ErrorMessage = "Код подтверждения обязателен")]
    [RegularExpression(@"^\d{4}$", ErrorMessage = "Код должен состоять из 4 цифр")]
    public string Code { get; set; } = string.Empty;
}

