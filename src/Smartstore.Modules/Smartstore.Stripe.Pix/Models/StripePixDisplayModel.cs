using Smartstore.Web.Modelling;

namespace Smartstore.StripeElements.Models;

public class StripePixDisplayModel : ModelBase
{
    public string QrCodeData { get; set; }
    public string QrCodeImageUrl { get; set; }
    public DateTime ExpiresAt { get; set; }
    public decimal Amount { get; set; }
    public string OrderGuid { get; set; }
    public string PaymentIntentId { get; set; }
}