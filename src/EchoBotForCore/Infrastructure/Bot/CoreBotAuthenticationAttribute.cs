using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.WebApiCompatShim;
using Microsoft.Bot.Connector;
using Newtonsoft.Json.Linq;

namespace EchoBotForCore.Infrastructure.Bot
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class CoreBotAuthententicationAttribute : Attribute, IAsyncActionFilter
    {

        public CoreBotAuthententicationAttribute(BotOptions botOptions)
        {
            BotOptions = botOptions;
        }

        public string MicrosoftAppId { get; set; }
        public string MicrosoftAppIdSettingName { get; set; }
        public bool DisableSelfIssuedTokens { get; set; }
        public virtual string OpenIdConfigurationUrl { get; set; } = JwtConfig.ToBotFromChannelOpenIdMetadataUrl;
        public BotOptions BotOptions { get; }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            MicrosoftAppId = MicrosoftAppId ?? BotOptions?.Authentication?.MicrosoftAppId ?? string.Empty;

            if (Debugger.IsAttached && String.IsNullOrEmpty(MicrosoftAppId))
            {
                // then auth is disabled
                await next();
                return;
            }


            var tokenExtractor = new JwtTokenExtractor(JwtConfig.GetToBotFromChannelTokenValidationParameters(MicrosoftAppId), OpenIdConfigurationUrl);
            var request = context.HttpContext.GetHttpRequestMessage();
            var identity = await tokenExtractor.GetIdentityAsync(request);

            // No identity? If we're allowed to, fall back to MSA
            // This code path is used by the emulator
            if (identity == null && !DisableSelfIssuedTokens)
            {
                tokenExtractor = new JwtTokenExtractor(JwtConfig.ToBotFromMSATokenValidationParameters, JwtConfig.ToBotFromMSAOpenIdMetadataUrl);
                identity = await tokenExtractor.GetIdentityAsync(request);

                // Check to make sure the app ID in the token is ours
                if (identity != null)
                {
                    // If it doesn't match, throw away the identity
                    if (tokenExtractor.GetBotIdFromClaimsIdentity(identity) != MicrosoftAppId)
                        identity = null;
                }
            }

            // Still no identity? Fail out.
            if (identity == null)
            {
                var host = request.RequestUri.DnsSafeHost;
                context.HttpContext.Response.Headers.Add("WWW-Authenticate", $"Bearer realm=\"{host}\"");
                context.Result = new StatusCodeResult(StatusCodes.Status401Unauthorized);
                return;
            }

            var activity = context.ActionArguments.Select(t => t.Value).OfType<Activity>().FirstOrDefault();
            if (activity != null)
            {
                MicrosoftAppCredentials.TrustServiceUrl(activity.ServiceUrl);
            }
            else
            {
                // No model binding to activity check if we can find JObject or JArray
                var obj = context.ActionArguments.Where(t => t.Value is JObject || t.Value is JArray).Select(t => t.Value).FirstOrDefault();
                if (obj != null)
                {
                    Activity[] activities = (obj is JObject) ? new Activity[] { ((JObject)obj).ToObject<Activity>() } : ((JArray)obj).ToObject<Activity[]>();
                    foreach (var jActivity in activities)
                    {
                        if (!string.IsNullOrEmpty(jActivity.ServiceUrl))
                        {
                            MicrosoftAppCredentials.TrustServiceUrl(jActivity.ServiceUrl);
                        }
                    }
                }
                else
                {
                    Trace.TraceWarning("No activity in the Bot Authentication Action Arguments");
                }
            }

            var principal = new ClaimsPrincipal(identity);
            Thread.CurrentPrincipal = principal;
            // Inside of ASP.NET this is required
            if (context.HttpContext != null)
                context.HttpContext.User = principal;
            await next();
        }
    }
}
