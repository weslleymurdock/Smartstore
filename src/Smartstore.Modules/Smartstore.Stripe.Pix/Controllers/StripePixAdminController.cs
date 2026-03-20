using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Smartstore.ComponentModel;
using Smartstore.Core.Data;
using Smartstore.Core.Security;
using Smartstore.Core.Stores;
using Smartstore.Engine.Modularity;
using Smartstore.StripeElements.Models;
using Smartstore.StripeElements.Providers;
using Smartstore.StripeElements.Services;
using Smartstore.StripeElements.Settings;
using Smartstore.Web.Controllers;
using Smartstore.Web.Modelling.Settings;

namespace Smartstore.StripeElements.Controllers;

[Area("Admin")]
[Route("admin/stripepix")] // Rota explícita ajuda a evitar conflitos
public class StripePixAdminController : ModuleController
{
    private readonly IProviderManager _providerManager;
    private readonly StripePixService _stripeHelper;

    public StripePixAdminController(
        IProviderManager providerManager, 
        StripePixService stripeHelper)
    {
        _providerManager = providerManager;
        _stripeHelper = stripeHelper;
    }

    [LoadSetting, AuthorizeAdmin]
    [HttpGet("configure")]
    public IActionResult Configure(StripePixSettings settings)
    {
        var provider = _providerManager.GetProvider(StripePixElementsProvider.SystemName);
        if (provider == null) return NotFound();

        ViewBag.Provider = provider.Metadata;

        var model = MiniMapper.Map<StripePixSettings, PixConfigurationModel>(settings);
        
        // Ajustado para apontar para o SEU controller de PIX, não o original do Stripe
        model.WebhookUrl = Url.Action("WebhookHandler", "StripePix", new { area = string.Empty }, Request.Scheme);

        return View(model);
    }

    [HttpPost("configure"), SaveSetting, AuthorizeAdmin]
    public IActionResult Configure(PixConfigurationModel model, StripePixSettings settings)
    {
        if (!ModelState.IsValid)
        {
            return Configure(settings);
        }

        ModelState.Clear();
        MiniMapper.Map(model, settings);

        // O Smartstore salva automaticamente via atributo [SaveSetting]
        NotifySuccess(T("Admin.Common.DataSuccessfullySaved"));

        return RedirectToAction(nameof(Configure));
    }

    [HttpPost("createwebhook")]
    [AuthorizeAdmin]
    public async Task<IActionResult> CreateWebhook()
    {
        var storeScope = GetActiveStoreScopeConfiguration();
        var settings = await Services.SettingFactory.LoadSettingsAsync<StripePixSettings>(storeScope);

        if (settings.SecrectApiKey.HasValue())
        {
            try
            {
                var store = storeScope == 0 ? Services.StoreContext.CurrentStore : Services.StoreContext.GetStoreById(storeScope);
                var storeUrl = store.GetBaseUrl();

                // O Service retorna o Secret do Webhook
                settings.WebhookSecret = await _stripeHelper.GetWebHookIdAsync(settings.SecrectApiKey, storeUrl);
                
                await Services.SettingFactory.SaveSettingsAsync(settings, storeScope);
                NotifySuccess(T("Plugins.Payments.Stripe.WebhookCreated"));
            }
            catch (Exception ex)
            {
                NotifyError(ex.Message);
            }
        }
        else
        {
            NotifyWarning(T("Plugins.Payments.Stripe.EnterApiKeysFirst"));
        }

        return RedirectToAction(nameof(Configure));
    }
}