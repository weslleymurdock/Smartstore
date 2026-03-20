using Smartstore.Web.Modelling;

namespace Smartstore.StripeElements.Models;

[LocalizedDisplay("Plugins.Smartstore.Stripe.")]
public class PixConfigurationModel : ModelBase
{
    [LocalizedDisplay("*PublicApiKey")]
    public string PublicApiKey { get; set; }

    [LocalizedDisplay("*SecrectApiKey")]
    public string SecrectApiKey { get; set; }

    [LocalizedDisplay("*WebhookSecret")]
    public string WebhookSecret { get; set; }

    [LocalizedDisplay("*WebhookUrl")]
    public string WebhookUrl { get; set; }

    [LocalizedDisplay("*AdditionalFee")]
    public decimal AdditionalFee { get; set; }

    [LocalizedDisplay("*AdditionalFeePercentage")]
    public bool AdditionalFeePercentage { get; set; }

    [LocalizedDisplay("*ShowButtonInMiniShoppingCart")]
    public bool ShowButtonInMiniShoppingCart { get; set; }

    [LocalizedDisplay("*ExpiresAfterSeconds")]
    public int ExpiresAfterSeconds { get; set; }
}