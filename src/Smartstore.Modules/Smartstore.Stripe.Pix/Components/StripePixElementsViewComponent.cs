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
    private readonly ICheckoutStateAccessor _checkoutStateAccessor;

    public StripePixElementsViewComponent(ICheckoutStateAccessor checkoutStateAccessor)
    {
        _checkoutStateAccessor = checkoutStateAccessor;
    }
    // public IViewComponentResult Invoke(string host)
    // {
    //     'confirm' é a zona que o checkout do Smartstore chama automaticamente 
    //     para métodos de pagamento que precisam de JS na última etapa.
    //     if (host == "confirm")
    //     {
    //         var state = _checkoutStateAccessor.CheckoutState.GetCustomState<StripePixCheckoutState>();
    //         if (state != null && state.PaymentIntentId.HasValue())
    //         {
    //             // Isso vai procurar em: Views/Shared/Components/StripePixElements/_CheckoutConfirm.cshtml
    //             // OU você pode passar o caminho completo:
    //             return View("_CheckoutConfirm", state);
    //         }
    //     }

    //     return Content(string.Empty);
    //     if (host == "confirm" || host == "payment-info")
    //     {
    //         var state = _checkoutStateAccessor.CheckoutState.GetCustomState<StripePixCheckoutState>();
    //         Remova a trava do 'state' apenas para testar se o HTML aparece
    //         return View("_CheckoutConfirm", state);
    //     }

    //     return Content("");
    // }
    public IViewComponentResult Invoke(string host)
    {
        // if (host == "payment-info")
        // {
        //     // O Smartstore passa o CheckoutPaymentMethodModel no parâmetro 'model'
        //     // do Widget. Vamos repassá-lo para a View.
        //     return View("PaymentInfo", model as Smartstore.Core.Checkout.Payment.ProcessPaymentRequest);
        // }

        if (host == "confirm")
        {
            var state = _checkoutStateAccessor.CheckoutState.GetCustomState<StripePixCheckoutState>();
            return View("_CheckoutConfirm", state ?? new StripePixCheckoutState());
        }

        return Content(string.Empty);
    }
}