using InternetShopService_back.Modules.OrderManagement.DTOs;
using InternetShopService_back.Modules.OrderManagement.Services;
using InternetShopService_back.Modules.UserCabinet.DTOs;
using InternetShopService_back.Modules.UserCabinet.Models;
using InternetShopService_back.Modules.UserCabinet.Repositories;
using InternetShopService_back.Shared.Models;
using InternetShopService_back.Shared.Repositories;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Modules.UserCabinet.Services;

public class CartService : ICartService
{
    private readonly ICartRepository _cartRepository;
    private readonly IUserAccountRepository _userAccountRepository;
    private readonly ICounterpartyRepository _counterpartyRepository;
    private readonly IOrderService _orderService;
    private readonly ILogger<CartService> _logger;

    public CartService(
        ICartRepository cartRepository,
        IUserAccountRepository userAccountRepository,
        ICounterpartyRepository counterpartyRepository,
        IOrderService orderService,
        ILogger<CartService> logger)
    {
        _cartRepository = cartRepository;
        _userAccountRepository = userAccountRepository;
        _counterpartyRepository = counterpartyRepository;
        _orderService = orderService;
        _logger = logger;
    }

    public async Task<CartDto> GetCartAsync(Guid userId)
    {
        var userAccount = await _userAccountRepository.GetByIdAsync(userId);
        if (userAccount == null)
        {
            throw new InvalidOperationException("Пользователь не найден");
        }

        var cart = await _cartRepository.GetByUserIdAsync(userId);
        
        // Создаем корзину, если её нет
        if (cart == null)
        {
            cart = new Cart
            {
                Id = Guid.NewGuid(),
                UserAccountId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            cart = await _cartRepository.CreateAsync(cart);
        }

        // Получаем скидки контрагента
        var discounts = await _counterpartyRepository.GetActiveDiscountsAsync(userAccount.CounterpartyId);

        // Преобразуем в DTO с применением скидок
        return MapToCartDto(cart, discounts);
    }

    public async Task<CartDto> AddItemAsync(Guid userId, AddCartItemDto item)
    {
        var userAccount = await _userAccountRepository.GetByIdAsync(userId);
        if (userAccount == null)
        {
            throw new InvalidOperationException("Пользователь не найден");
        }

        var cart = await _cartRepository.GetByUserIdAsync(userId);
        
        // Создаем корзину, если её нет
        if (cart == null)
        {
            cart = new Cart
            {
                Id = Guid.NewGuid(),
                UserAccountId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            cart = await _cartRepository.CreateAsync(cart);
        }

        // Проверяем, есть ли уже такой товар в корзине
        var existingItem = cart.Items.FirstOrDefault(i => i.NomenclatureId == item.NomenclatureId);
        
        if (existingItem != null)
        {
            // Увеличиваем количество
            existingItem.Quantity += item.Quantity;
            existingItem.Price = item.Price; // Обновляем цену на актуальную
            existingItem.UpdatedAt = DateTime.UtcNow;
            await _cartRepository.UpdateCartItemAsync(existingItem);
        }
        else
        {
            // Добавляем новый товар
            var cartItem = new CartItem
            {
                Id = Guid.NewGuid(),
                CartId = cart.Id,
                NomenclatureId = item.NomenclatureId,
                NomenclatureName = item.NomenclatureName,
                Quantity = item.Quantity,
                Price = item.Price,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _cartRepository.AddCartItemAsync(cartItem);
        }

        // Обновляем время изменения корзины
        cart.UpdatedAt = DateTime.UtcNow;
        await _cartRepository.UpdateAsync(cart);

        // Получаем обновленную корзину со скидками
        cart = await _cartRepository.GetByUserIdAsync(userId);
        var discounts = await _counterpartyRepository.GetActiveDiscountsAsync(userAccount.CounterpartyId);
        
        return MapToCartDto(cart!, discounts);
    }

    public async Task<CartDto> UpdateItemAsync(Guid userId, Guid itemId, int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Количество должно быть больше нуля");
        }

        var userAccount = await _userAccountRepository.GetByIdAsync(userId);
        if (userAccount == null)
        {
            throw new InvalidOperationException("Пользователь не найден");
        }

        var cartItem = await _cartRepository.GetCartItemByIdAsync(itemId);
        if (cartItem == null)
        {
            throw new InvalidOperationException("Товар не найден в корзине");
        }

        // Проверяем, что товар принадлежит корзине пользователя
        var cart = await _cartRepository.GetByUserIdAsync(userId);
        if (cart == null || cartItem.CartId != cart.Id)
        {
            throw new UnauthorizedAccessException("Товар не принадлежит вашей корзине");
        }

        cartItem.Quantity = quantity;
        cartItem.UpdatedAt = DateTime.UtcNow;
        await _cartRepository.UpdateCartItemAsync(cartItem);

        // Обновляем время изменения корзины
        cart.UpdatedAt = DateTime.UtcNow;
        await _cartRepository.UpdateAsync(cart);

        // Получаем обновленную корзину со скидками
        cart = await _cartRepository.GetByUserIdAsync(userId);
        var discounts = await _counterpartyRepository.GetActiveDiscountsAsync(userAccount.CounterpartyId);
        
        return MapToCartDto(cart!, discounts);
    }

    public async Task<bool> RemoveItemAsync(Guid userId, Guid itemId)
    {
        var cartItem = await _cartRepository.GetCartItemByIdAsync(itemId);
        if (cartItem == null)
        {
            return false;
        }

        // Проверяем, что товар принадлежит корзине пользователя
        var cart = await _cartRepository.GetByUserIdAsync(userId);
        if (cart == null || cartItem.CartId != cart.Id)
        {
            throw new UnauthorizedAccessException("Товар не принадлежит вашей корзине");
        }

        var result = await _cartRepository.RemoveCartItemAsync(itemId);
        
        if (result)
        {
            // Обновляем время изменения корзины
            cart.UpdatedAt = DateTime.UtcNow;
            await _cartRepository.UpdateAsync(cart);
        }

        return result;
    }

    public async Task<bool> ClearCartAsync(Guid userId)
    {
        var cart = await _cartRepository.GetByUserIdAsync(userId);
        if (cart == null)
        {
            return false;
        }

        await _cartRepository.ClearCartItemsAsync(cart.Id);
        
        // Обновляем время изменения корзины
        cart.UpdatedAt = DateTime.UtcNow;
        await _cartRepository.UpdateAsync(cart);

        return true;
    }

    private CartDto MapToCartDto(Cart cart, List<Discount> discounts)
    {
        var items = cart.Items.Select(item =>
        {
            // Применяем скидку к товару
            var discount = FindDiscountForItem(item.NomenclatureId, discounts);
            var discountPercent = discount?.DiscountPercent ?? 0;
            
            var priceWithDiscount = item.Price * (1 - discountPercent / 100);
            var totalAmount = priceWithDiscount * item.Quantity;

            return new CartItemDto
            {
                Id = item.Id,
                NomenclatureId = item.NomenclatureId,
                NomenclatureName = item.NomenclatureName,
                Quantity = item.Quantity,
                Price = item.Price,
                DiscountPercent = discountPercent,
                PriceWithDiscount = priceWithDiscount,
                TotalAmount = totalAmount
            };
        }).ToList();

        var totalAmount = items.Sum(i => i.TotalAmount);

        return new CartDto
        {
            Id = cart.Id,
            Items = items,
            TotalAmount = totalAmount
        };
    }

    private Discount? FindDiscountForItem(Guid nomenclatureId, List<Discount> discounts)
    {
        // Сначала ищем скидку на конкретную позицию
        var itemDiscount = discounts.FirstOrDefault(d => 
            d.NomenclatureId == nomenclatureId && d.NomenclatureGroupId == null);
        
        if (itemDiscount != null)
        {
            return itemDiscount;
        }

        // Если скидки на конкретную позицию нет, ищем скидку на группу
        // Здесь нужно будет получить информацию о группе номенклатуры из FimBiz
        // Пока возвращаем null, так как у нас нет информации о группах в текущей модели
        
        // TODO: Получить группу номенклатуры и найти скидку на группу
        return null;
    }

    public async Task<OrderDto> CreateOrderFromCartAsync(Guid userId, CreateOrderFromCartDto dto)
    {
        var userAccount = await _userAccountRepository.GetByIdAsync(userId);
        if (userAccount == null)
        {
            throw new InvalidOperationException("Пользователь не найден");
        }

        // Получаем корзину пользователя
        var cart = await _cartRepository.GetByUserIdAsync(userId);
        if (cart == null || !cart.Items.Any())
        {
            throw new InvalidOperationException("Корзина пуста");
        }

        // Получаем скидки контрагента
        var discounts = await _counterpartyRepository.GetActiveDiscountsAsync(userAccount.CounterpartyId);

        // Преобразуем товары из корзины в формат для заказа
        var orderItems = cart.Items.Select(item =>
        {
            var discount = FindDiscountForItem(item.NomenclatureId, discounts);
            var discountPercent = discount?.DiscountPercent ?? 0;

            return new CreateOrderItemDto
            {
                NomenclatureId = item.NomenclatureId,
                NomenclatureName = item.NomenclatureName,
                Quantity = item.Quantity,
                Price = item.Price
            };
        }).ToList();

        // Создаем заказ через OrderService
        var order = await _orderService.CreateOrderFromCartAsync(userId, dto, orderItems);

        // Очищаем корзину после успешного создания заказа
        await _cartRepository.ClearCartItemsAsync(cart.Id);
        cart.UpdatedAt = DateTime.UtcNow;
        await _cartRepository.UpdateAsync(cart);

        _logger.LogInformation("Создан заказ {OrderId} из корзины пользователя {UserId}", order.Id, userId);

        return order;
    }
}
