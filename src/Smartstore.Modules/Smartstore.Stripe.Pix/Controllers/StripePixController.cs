using System.IO; 
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Smartstore.Core.Catalog.Attributes;
using Smartstore.Core.Catalog.Pricing;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Checkout.Shipping;
using Smartstore.Core.Checkout.Tax;
using Smartstore.Core.Common.Services;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Core.Stores; 
using Smartstore.StripeElements.Models;
using Smartstore.StripeElements.Providers;
using Smartstore.StripeElements.Services;
using Smartstore.StripeElements.Settings;
using Smartstore.Utilities.Html;
using Smartstore.Web.Controllers;

namespace Smartstore.StripeElements.Controllers;

public class StripePixController : ModuleController
{
    private readonly SmartDbContext _db;
    private readonly StripePixSettings _settings;
    private readonly ICheckoutStateAccessor _checkoutStateAccessor;
    private readonly ICheckoutWorkflow _checkoutWorkflow;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly ITaxService _taxService;
    private readonly IPriceCalculationService _priceCalculationService;
    private readonly IProductService _productService;
    private readonly IOrderCalculationService _orderCalculationService;
    private readonly ICurrencyService _currencyService;
    private readonly IRoundingHelper _roundingHelper;
    private readonly IOrderProcessingService _orderProcessingService;
    private readonly StripePixService _stripeHelper;

    public StripePixController(
        SmartDbContext db,
        StripePixSettings settings,
        ICheckoutStateAccessor checkoutStateAccessor,
        ICheckoutWorkflow checkoutWorkflow,
        IShoppingCartService shoppingCartService,
        ITaxService taxService,
        IPriceCalculationService priceCalculationService,
        IProductService productService,
        IOrderCalculationService orderCalculationService,
        ICurrencyService currencyService,
        IRoundingHelper roundingHelper,
        IOrderProcessingService orderProcessingService,
        StripePixService stripeHelper)
    {
        _db = db;
        _settings = settings;
        _checkoutStateAccessor = checkoutStateAccessor;
        _checkoutWorkflow = checkoutWorkflow;
        _shoppingCartService = shoppingCartService;
        _taxService = taxService;
        _priceCalculationService = priceCalculationService;
        _productService = productService;
        _orderCalculationService = orderCalculationService;
        _currencyService = currencyService;
        _roundingHelper = roundingHelper;
        _orderProcessingService = orderProcessingService;
        _stripeHelper = stripeHelper;
    }

    [HttpPost]
    public async Task<IActionResult> ValidateCart(ProductVariantQuery query, bool? useRewardPoints)
    {
        var success = false;
        var message = string.Empty;
        var store = Services.StoreContext.CurrentStore;
        var customer = Services.WorkContext.CurrentCustomer;
        var warnings = new List<string>();
        var cart = await _shoppingCartService.GetCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

        var isCartValid = await _shoppingCartService.SaveCartDataAsync(cart, warnings, query, useRewardPoints, false);
        if (isCartValid)
        {
            success = true;
        }
        else
        {
            message = string.Join(Environment.NewLine, warnings);
        }

        return Json(new { success, message });
    }


    /// <summary>
    /// AJAX
    /// Called after buyer clicked buy-now-button but before the order was created.
    /// Processes payment and return redirect URL if there is any.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ConfirmOrder(string formData)
    {
        string redirectUrl = null;
        var messages = new List<string>();
        var success = false;

        try
        {
            var store = Services.StoreContext.CurrentStore;
            var customer = Services.WorkContext.CurrentCustomer;

            if (!HttpContext.Session.TryGetObject<ProcessPaymentRequest>("OrderPaymentInfo", out var paymentRequest) || paymentRequest == null)
            {
                paymentRequest = new ProcessPaymentRequest();
            }


            paymentRequest.StoreId = store.Id;
            paymentRequest.CustomerId = customer.Id;
            paymentRequest.PaymentMethodSystemName = StripePixElementsProvider.SystemName;

            // We must check here if an order can be placed to avoid creating unauthorized transactions.
            var (warnings, cart) = await _orderProcessingService.ValidateOrderPlacementAsync(paymentRequest);
            if (warnings.Count == 0)
            {
                if (await _orderProcessingService.IsMinimumOrderPlacementIntervalValidAsync(customer, store))
                {
                    var state = _checkoutStateAccessor.CheckoutState.GetCustomState<StripePixCheckoutState>();
                    var cartTotal = await _orderCalculationService.GetShoppingCartTotalAsync(cart, true);
                    var convertedTotal = cartTotal.ConvertedAmount.Total.Value;

                    var paymentIntentService = new PaymentIntentService();
                    PaymentIntent paymentIntent = null;

                    var shippingOption = customer.GenericAttributes.Get<ShippingOption>(SystemCustomerAttributeNames.SelectedShippingOption, store.Id);
                    var shipping = shippingOption != null
                        ? await GetShippingAddressAsync(customer, shippingOption.Name)
                        : null;

                    // Criar ou Atualizar Intent específica para PIX
                    var options = await _stripeHelper.CreatePixPaymentIntentOptionsAsync(paymentRequest, _settings);

                    if (!state.PaymentIntentId.HasValue())
                    {
                        paymentIntent = await paymentIntentService.CreateAsync(options);

                        state.PaymentIntentId = paymentIntent.Id;
                    }
                    else
                    {
                        // Update Stripe Payment Intent.
                        var intentUpdateOptions = new PaymentIntentUpdateOptions
                        {
                            Amount = _roundingHelper.ToSmallestCurrencyUnit(convertedTotal),
                            Currency = Services.WorkContext.WorkingCurrency.CurrencyCode.ToLower(),
                            PaymentMethod = state.PaymentMethod,
                            Shipping = shipping
                        };

                        paymentIntent = await paymentIntentService.UpdateAsync(state.PaymentIntentId, intentUpdateOptions);
                    }

                    // Confirm the intent to generate the QR Code
                    var confirmOptions = new PaymentIntentConfirmOptions
                    {
                        ReturnUrl = store.GetAbsoluteUrl(Url.Action("RedirectionResult", "StripePix").TrimStart('/')),
                        PaymentMethodData = new PaymentIntentPaymentMethodDataOptions { Type = "pix" }
                    };

                    paymentIntent = await paymentIntentService.ConfirmAsync(paymentIntent.Id, confirmOptions);

                    // Stripe returns an "NextAction" of type "pix_display_qr_code"
                    if (paymentIntent.Status == "requires_action")
                    {
                        // We redirect to an action which displays the QR Code
                        redirectUrl = Url.Action("DisplayQrCode", "StripePix", new { id = paymentIntent.Id });
                    }

                    success = true;
                    state.IsConfirmed = true;
                    return Json(new { success, redirectUrl });
                }
                else
                {
                    messages.Add(T("Checkout.MinOrderPlacementInterval"));
                }
            }
            else
            {
                messages.AddRange(warnings.Select(HtmlUtility.ConvertPlainTextToHtml));
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            messages.Add(ex.Message);
        }

        return Json(new { success, redirectUrl, messages });
    }

    [HttpGet]
    public async Task<IActionResult> DisplayQrCode(string id)
    {
        var service = new PaymentIntentService();
        var intent = await service.GetAsync(id);

        if (intent?.NextAction?.PixDisplayQrCode == null)
        {
            Logger.Warn("Stripe PIX: NextAction ou PixDisplayQrCode está nulo para a Intent {0}", id);
            return RedirectToAction("PaymentMethod", "Checkout");
        }

        var model = new StripePixDisplayModel
        {
            QrCodeData = intent.NextAction.PixDisplayQrCode.Data,
            QrCodeImageUrl = intent.NextAction.PixDisplayQrCode.ImageUrlPng, // Se disponível ou use gerador local
            ExpiresAt = intent.NextAction.PixDisplayQrCode.ExpiresAt,
            Amount = intent.Amount / 100M
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> CheckPaymentStatus(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Json(new { paid = false });

        var order = await _db.Orders
            .AsNoTracking()
            .Where(x => x.AuthorizationTransactionId == id)
            .Select(x => new { x.PaymentStatus }) // Seleciona apenas o que precisa
            .FirstOrDefaultAsync();

        return Json(new { paid = order?.PaymentStatus == PaymentStatus.Paid });
    }

    private async Task<ChargeShippingOptions> GetShippingAddressAsync(Core.Identity.Customer customer, string carrier)
    {
        var address = customer.ShippingAddress ?? customer.BillingAddress;
        var country = await _db.Countries.FindAsync(address.CountryId);

        return new ChargeShippingOptions
        {
            Carrier = carrier,
            Name = $"{address.FirstName} {address.LastName}",
            Address = new AddressOptions
            {
                City = address.City,
                Country = country.TwoLetterIsoCode,
                Line1 = address.Address1,
                Line2 = address.Address2,
                PostalCode = address.ZipPostalCode
            }
        };
    }

    public async Task<IActionResult> RedirectionResult(string redirect_status, string payment_intent)
    {
        var error = false;
        string message = null;
        var success = redirect_status == "succeeded" || redirect_status == "pending" || !redirect_status.HasValue();

        //Logger.LogInformation($"Stripe redirection result: '{redirect_status}'");

        // INFO: In case of declined payment when checking card data with 3D Secure redirection
        // we must check the status of the payment intend for 'requires_payment_method' which means the payment was declined.
        var paymentIntentService = new PaymentIntentService();
        PaymentIntent paymentIntent = await paymentIntentService.GetAsync(payment_intent);

        if (success && paymentIntent.Status != "requires_payment_method")
        {
            var state = _checkoutStateAccessor.CheckoutState.GetCustomState<StripePixCheckoutState>();
            if (state.PaymentIntentId.HasValue())
            {
                state.SubmitForm = true;
            }
            else
            {
                error = true;
                message = T("Payment.MissingCheckoutState", "StripeCheckoutState." + nameof(state.PaymentIntentId));
            }
        }
        else
        {
            error = true;
            message = T("Payment.PaymentFailure");
        }

        if (error)
        {
            _checkoutStateAccessor.CheckoutState.RemoveCustomState<StripePixCheckoutState>();
            NotifyWarning(message);

            return RedirectToAction(nameof(CheckoutController.PaymentMethod), "Checkout");
        }

        return RedirectToAction(nameof(CheckoutController.Confirm), "Checkout");
    }


    [HttpPost]
    [Route("stripe/pix/webhookhandler"), WebhookEndpoint]
    public async Task<IActionResult> WebhookHandler()
    {
        using var reader = new StreamReader(HttpContext.Request.Body, leaveOpen: true);
        var json = await reader.ReadToEndAsync();
        var endpointSecret = _settings.WebhookSecret;

        try
        {
            var signatureHeader = Request.Headers["Stripe-Signature"];

            // INFO: There should never be a version mismatch, as long as the hook was created in Smartstore backend.
            // But to keep even more stable we don't throw an exception on API version mismatch.
            var stripeEvent = EventUtility.ParseEvent(json, false);
            stripeEvent = EventUtility.ConstructEvent(json, signatureHeader, endpointSecret, throwOnApiVersionMismatch: false);

            if (stripeEvent.Type == EventTypes.PaymentIntentSucceeded)
            {
                // Payment intent was captured in Stripe backend
                var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
                var order = await GetStripeOrderAsync(paymentIntent.Id);

                if (order != null)
                {
                    var settings = await Services.SettingFactory.LoadSettingsAsync<StripePixSettings>(order.StoreId);

                    // INFO: This can also be a partial capture.
                    decimal convertedAmount = paymentIntent.Amount / 100M;

                    // Check if full order amount was captured.
                    if (order.OrderTotal == convertedAmount)
                    {
                        if (order.CanMarkOrderAsPaid())
                        {
                            await _orderProcessingService.MarkOrderAsPaidAsync(order);
                        }
                        else if (order.CanMarkOrderAsAuthorized())
                        {
                            await _orderProcessingService.MarkAsAuthorizedAsync(order);
                        }
                    }
                    else
                    {
                        order.PaymentStatus = PaymentStatus.Pending;
                        await _db.SaveChangesAsync();
                    }
                }
                else
                {
                    // The order may not have been created yet. Let Stripe send the hook again. 
                    return StatusCode(500);
                }
            }
            else if (stripeEvent.Type == EventTypes.ChargeRefunded)
            {
                var charge = stripeEvent.Data.Object as Charge;
                var order = await GetStripeOrderAsync(charge.PaymentIntentId);

                if (order != null)
                {
                    decimal convertedAmount = charge.Amount / 100M;

                    if (order.OrderTotal == convertedAmount)
                    {
                        if (order.CanRefundOffline())
                        {
                            await _orderProcessingService.RefundOfflineAsync(order);
                        }
                    }
                    else if (order.CanPartiallyRefundOffline(convertedAmount))
                    {
                        await _orderProcessingService.PartiallyRefundOfflineAsync(order, convertedAmount);
                    }
                }
            }
            else if (stripeEvent.Type == EventTypes.PaymentIntentCanceled ||
                     stripeEvent.Type == EventTypes.PaymentIntentPaymentFailed)
            {
                var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
                var order = await GetStripeOrderAsync(paymentIntent.Id);

                if (order != null && order.CanVoidOffline())
                {
                    await _orderProcessingService.VoidOfflineAsync(order);
                }
            }
            else
            {
                Logger.Warn("Unhandled Stripe event type: {0}", stripeEvent.Type);
            }

            return Ok();
        }
        catch (StripeException ex)
        {
            Logger.Error(ex);
            return BadRequest();
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            return StatusCode(500);
        }
    }

    private async Task<Order> GetStripeOrderAsync(string paymentIntentId)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(x =>
                    x.PaymentMethodSystemName == StripePixElementsProvider.SystemName &&
                    x.AuthorizationTransactionId == paymentIntentId);

        if (order == null)
        {
            Logger.Warn(T("Plugins.Smartstore.Stripe.Pix.OrderNotFound", paymentIntentId));
            return null;
        }

        return order;
    }

    // INFO: We leave this method in case we want to log further infos in future.
    private void WriteOrderNotes(Order order, Charge charge)
    {
        if (charge != null)
        {
            _db.OrderNotes.Add(order, $"Reason for Charge-ID {charge.Id}: {charge.Refunds?.FirstOrDefault()?.Reason} - {charge.Description}", true);
        }
    }
}