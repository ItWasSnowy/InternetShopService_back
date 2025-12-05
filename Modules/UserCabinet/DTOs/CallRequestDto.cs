using System.ComponentModel.DataAnnotations;

namespace InternetShopService_back.Modules.UserCabinet.DTOs;

public class CallRequestDto
{
    [Required(ErrorMessage = "Номер телефона обязателен")]
    [RegularExpression(@"^7\d{10}$", ErrorMessage = "Номер телефона должен быть в формате 7XXXXXXXXXX")]
    public string PhoneNumber { get; set; } = string.Empty;
}

