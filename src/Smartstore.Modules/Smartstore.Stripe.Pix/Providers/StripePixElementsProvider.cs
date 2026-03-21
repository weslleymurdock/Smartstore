using DotLiquid.Util;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Smartstore.Caching;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Common.Services;
using Smartstore.Core.Configuration;
using Smartstore.Core.Data;
using Smartstore.Core.Stores;
using Smartstore.Core.Widgets;
using Smartstore.Engine.Modularity;
using Smartstore.Http;
using Smartstore.StripeElements.Components;
using Smartstore.StripeElements.Controllers;
using Smartstore.StripeElements.Models;
using Smartstore.StripeElements.Settings;

namespace Smartstore.StripeElements.Providers;

[SystemName("Payments.StripeElements.Pix")]
[FriendlyName("Pix Stripe Elements")]
[Order(1)]
[PaymentMethod(PaymentMethodType.Standard | PaymentMethodType.Unknown)]
public class StripePixElementsProvider : PaymentMethodBase, IConfigurable
{
    private readonly SmartDbContext _db;
    private readonly IStoreContext _storeContext;
    private readonly ISettingFactory _settingFactory;
    private readonly ICheckoutStateAccessor _checkoutStateAccessor;
    private readonly IRoundingHelper _roundingHelper;
    private readonly ICacheManager _cache;
    private readonly StripePixSettings _settings;

    public StripePixElementsProvider(
        SmartDbContext db,
        IStoreContext storeContext,
        ISettingFactory settingFactory,
        ICheckoutStateAccessor checkoutStateAccessor,
        IRoundingHelper roundingHelper,
        ICacheManager cache,
        StripePixSettings settings)
    {
        _db = db;
        _storeContext = storeContext;
        _settingFactory = settingFactory;
        _checkoutStateAccessor = checkoutStateAccessor;
        _roundingHelper = roundingHelper;
        _cache = cache;
        _settings = settings;

        // Ensure API is set with current module settings. 
        if (StripeConfiguration.ApiKey != _settings.SecrectApiKey)
        {
            StripeConfiguration.ApiKey = _settings.SecrectApiKey;
        }
    }

    public ILogger Logger { get; set; } = NullLogger.Instance;

    public static string SystemName => "Payments.StripeElements.Pix";

    public override bool SupportCapture => false;

    public override bool SupportVoid => true;

    public override bool SupportPartiallyRefund => true;

    public override bool SupportRefund => true;

    public override bool RequiresInteraction => true;

    public RouteInfo GetConfigurationRoute()
        => new(nameof(StripePixAdminController.Configure), "StripePixAdmin", new { area = "Admin" });

    public override Widget GetPaymentInfoWidget()
        => new ComponentWidget(typeof(StripePixElementsViewComponent), new { host = "display" });

    public override Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
    {
        var request = new ProcessPaymentRequest
        {
            OrderGuid = Guid.NewGuid()
        };

        return Task.FromResult(request);
    }

    public override Task<string> GetConfirmationUrlAsync(ProcessPaymentRequest request, CheckoutContext context)
    {
        return base.GetConfirmationUrlAsync(request, context);
    }

    public override async Task<(decimal FixedFeeOrPercentage, bool UsePercentage)> GetPaymentFeeInfoAsync(ShoppingCart cart)
    {
        var settings = await _settingFactory.LoadSettingsAsync<StripePixSettings>(_storeContext.CurrentStore.Id);

        return (settings.AdditionalFee, settings.AdditionalFeePercentage);
    }

    public override async Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest request)
    {
        var result = new RefundPaymentResult
        {
            NewPaymentStatus = request.Order.PaymentStatus
        };

        try
        {
            // set the refund options
            var options = new RefundCreateOptions
            {
                PaymentIntent = request.Order.AuthorizationTransactionId
            };

            // if partial, set the value, otherwise stripe will refund the total value
            if (request.IsPartialRefund)
            {
                options.Amount = _roundingHelper.ToSmallestCurrencyUnit(request.AmountToRefund);
            }

            var service = new RefundService();
            var refund = await service.CreateAsync(options);

            if (refund?.Id != null)
            {
                // 2. save the id of refund for audit
                var attributeKey = "Payments.StripeElements.Pix.RefundId";
                var refundIds = request.Order.GenericAttributes.Get<List<string>>(attributeKey) ?? new List<string>();

                if (!refundIds.Contains(refund.Id))
                {
                    refundIds.Add(refund.Id);
                    request.Order.GenericAttributes.Set(attributeKey, refundIds);

                    // add a note into the order to the admidn
                    _db.OrderNotes.Add(new OrderNote
                    {
                        OrderId = request.Order.Id,
                        Note = $"Stripe PIX Refund created. ID: {refund.Id}. Status: {refund.Status}",
                        DisplayToCustomer = false,
                        CreatedOnUtc = DateTime.UtcNow
                    });

                    await _db.SaveChangesAsync();
                }

                // 3. Update the payment status into the result
                result.NewPaymentStatus = request.IsPartialRefund
                    ? PaymentStatus.PartiallyRefunded
                    : PaymentStatus.Refunded;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error while refunding Stripe PIX payment.");
        }

        return result;
    }
    public override Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest request)
    {
        // Para PIX, retornamos Pendente. A Intent será confirmada no Controller.
        return Task.FromResult(new ProcessPaymentResult
        {
            NewPaymentStatus = PaymentStatus.Pending,
        });
    }

    public override async Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest request)
    {
        // No PIX, Void = Cancelar a Intent de pagamento ainda não paga
        var result = new VoidPaymentResult();
        try
        {
            var service = new PaymentIntentService();
            await service.CancelAsync(request.Order.AuthorizationTransactionId);
            result.NewPaymentStatus = PaymentStatus.Voided;
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
        }
        return result;
    }

}