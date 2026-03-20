using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Smartstore.Core;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Data;
using Smartstore.Core.Widgets;
using Smartstore.StripeElements.Models;
using Smartstore.StripeElements.Providers;
using Smartstore.StripeElements.Services;
using Smartstore.StripeElements.Settings;
using Smartstore.Web.Controllers;

namespace Smartstore.StripeElements.Filters;

public class PixCheckoutFilter : IAsyncResultFilter
{
    private readonly SmartDbContext _db;
    private readonly ICommonServices _services;
    private readonly IPaymentService _paymentService;
    private readonly StripePixSettings _settings;
    private readonly ICheckoutStateAccessor _checkoutStateAccessor;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IWidgetProvider _widgetProvider;
    private readonly StripePixService _service;

    public PixCheckoutFilter(
        SmartDbContext db,
        ICommonServices services,
        IPaymentService paymentService,
        StripePixSettings settings,
        ICheckoutStateAccessor checkoutStateAccessor,
        IHttpContextAccessor httpContextAccessor,
        IWidgetProvider widgetProvider,
        StripePixService service)
    {
        _db = db;
        _services = services;
        _paymentService = paymentService;
        _settings = settings;
        _checkoutStateAccessor = checkoutStateAccessor;
        _httpContextAccessor = httpContextAccessor;
        _widgetProvider = widgetProvider;
        _service = service;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext filterContext, ResultExecutionDelegate next)
    {

        await next();

        var customer = _services.WorkContext.CurrentCustomer;
        var action = filterContext.RouteData.Values.GetActionName();

        if (action.EqualsNoCase(nameof(CheckoutController.PaymentMethod)))
        {
            var checkoutState = _checkoutStateAccessor.CheckoutState.GetCustomState<StripePixCheckoutState>();
        

            // Should only run on a full view rendering result or HTML ContentResult.
            if (filterContext.Result is StatusCodeResult || filterContext.Result.IsHtmlViewResult())
            {
                customer.GenericAttributes.SelectedPaymentMethod = StripePixElementsProvider.SystemName;
                await _db.SaveChangesAsync();

                var session = _httpContextAccessor.HttpContext.Session;
                if (!session.ContainsKey("OrderPaymentInfo"))
                {
                    session.TrySetObject("OrderPaymentInfo", new ProcessPaymentRequest
                    {
                        StoreId = _services.StoreContext.CurrentStore.Id,
                        CustomerId = customer.Id,
                        PaymentMethodSystemName = StripePixElementsProvider.SystemName,
                        
                    });
                }

                filterContext.Result = new RedirectToActionResult(nameof(CheckoutController.Confirm), "Checkout", new { area = string.Empty });
            }
        }
        else if (action.EqualsNoCase(nameof(CheckoutController.Confirm)))
        {
            if (customer.GenericAttributes.SelectedPaymentMethod.EqualsNoCase(StripePixElementsProvider.SystemName))
            {
                var state = _checkoutStateAccessor.CheckoutState;

                if (state.IsPaymentRequired)
                {
                    _widgetProvider.RegisterWidget("end",
                        new PartialViewWidget("_CheckoutConfirm", state.GetCustomState<StripePixCheckoutState>(), "Smartstore.Stripe.Pix"));
                }
            }
        }

        await next();
    }
}