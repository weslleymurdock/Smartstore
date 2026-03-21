using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Common.Services;
using Smartstore.Json;
using Smartstore.StripeElements.Models;
using Smartstore.StripeElements.Services;
using Smartstore.StripeElements.Settings;
using Smartstore.Web.Components;

namespace Smartstore.StripeElements.Components;

public class StripePixElementsViewComponent : SmartViewComponent
{
    private readonly StripePixSettings _settings;
    private readonly ICheckoutStateAccessor _checkoutStateAccessor;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly IOrderCalculationService _orderCalculationService;
    private readonly ICurrencyService _currencyService;
    private readonly IRoundingHelper _roundingHelper;
    private readonly StripePixService _service;

    public StripePixElementsViewComponent(
        ICheckoutStateAccessor checkoutStateAccessor,
        StripePixSettings settings,
        IShoppingCartService shoppingCartService,
        IOrderCalculationService orderCalculationService,
        ICurrencyService currencyService,
        IRoundingHelper roundingHelper,
        StripePixService service)
    {
        _checkoutStateAccessor = checkoutStateAccessor;
        _settings = settings;
        _shoppingCartService = shoppingCartService;
        _orderCalculationService = orderCalculationService;
        _currencyService = currencyService;
        _roundingHelper = roundingHelper;
        _service = service;
    }

    public IViewComponentResult Invoke(string host, object model)
    {

         // If public API key or secret API key haven't been configured yet, don't render anything.
        // if (!_settings.PublicApiKey.HasValue() || !_settings.SecrectApiKey.HasValue())
        // {
        //     return Empty();
        // }

        // var routeIdent = Request.RouteValues.GenerateRouteIdentifier();
        // var isPaymentSelectionPage = routeIdent == "Checkout.PaymentMethod" || routeIdent == "Checkout.PaymentInfoAjax";

        // var model = new PublicStripeElementsModel
        // {
        //     PublicApiKey = _settings.PublicApiKey,
        //     IsPaymentSelectionPage = isPaymentSelectionPage,
        //     IsCartPage = routeIdent == "ShoppingCart.Cart" || routeIdent == "ShoppingCart.UpdateCartItem"
        // };

        // if (isPaymentSelectionPage)
        // {
        //     var store = Services.StoreContext.CurrentStore;
        //     var customer = Services.WorkContext.CurrentCustomer;
        //     var currency = Services.WorkContext.WorkingCurrency;
        //     var cart = await _shoppingCartService.GetCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

        //     // Get subtotal
        //     var cartSubTotal = await _orderCalculationService.GetShoppingCartSubtotalAsync(cart, true);
        //     var subTotalConverted = _currencyService.ConvertFromPrimaryCurrency(cartSubTotal.SubtotalWithDiscount.Amount, currency);

        //     model.Amount = _roundingHelper.ToSmallestCurrencyUnit(subTotalConverted);
        //     model.Currency = currency.CurrencyCode.ToLower();
        //     model.CaptureMethod = _settings.CaptureMethod;

        //     return View(model);
        // }

        // var stripePaymentRequest = await _service.GetStripePaymentRequestAsync();

        // model.PaymentRequest = JsonSerializer.Serialize(stripePaymentRequest, SmartJsonOptions.CamelCasedIgnoreDefaults);

        // return View(model);

        if (host == "confirm")
        {
            // Ignoramos o 'model' que vem da página de checkout (que causa o mismatch)
            // e pegamos o nosso estado específico do banco/sessão.
            var state = _checkoutStateAccessor.CheckoutState.GetCustomState<StripePixCheckoutState>();

            return View("_CheckoutConfirm", state ?? new StripePixCheckoutState());
        }

        if (host == "payment-info")
        {
            return View("PaymentInfo", model);
        }

        if (host == "display")
        {
            return View("DisplayQrCode", model);
        }
        
        return Content(string.Empty);
    }
}