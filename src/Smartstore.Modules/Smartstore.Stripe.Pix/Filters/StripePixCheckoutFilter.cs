using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Widgets;
using Smartstore.StripeElements.Components;
using Smartstore.StripeElements.Models;
using Smartstore.StripeElements.Services;
using Smartstore.StripeElements.Settings;

namespace Smartstore.StripeElements.Filters;

public class StripePixCheckoutFilter : IAsyncResultFilter
{
    private readonly IWidgetProvider _widgetProvider;
    private readonly ICheckoutStateAccessor _checkoutStateAccessor;

    public StripePixCheckoutFilter(IWidgetProvider widgetProvider, ICheckoutStateAccessor checkoutStateAccessor)
    {
        _widgetProvider = widgetProvider;
        _checkoutStateAccessor = checkoutStateAccessor;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        // Verifica se é o resultado de uma View
        if (context.Result.IsHtmlViewResult())
        {
            var state = _checkoutStateAccessor.CheckoutState.GetCustomState<StripePixCheckoutState>();

            // Injeta o componente na zona de confirmação se houver uma Intent ativa
            if (state != null && state.PaymentIntentId.HasValue())
            {
                // "checkout_confirm_top" é a zona padrão do Smartstore para scripts de checkout
                _widgetProvider.RegisterViewComponent<StripePixElementsViewComponent>("checkout_confirm_top", new { host = "confirm" });
            }
        }

        await next();
    }
}
