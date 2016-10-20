# ASP.NET Core Bot Authentication

This repository shows how you can use ASP.NET Core and authenticate your Bot.

# Approach

The Bot Framework C# SDK currently targets full .NET and makes use of System.Configuration for grabbing your Microsoft Application ID and Key.
The key to getting this to work is to provide an alternative mechanism to providing those key credentials.

# Implementation

## BotOptions

`BotOptions` are POCO *option* classes which are populated using the new Options capabilities which are a part of the ASP.NET core configuration system.

```C#
public class BotOptions
{
    public BotAuthenticationOptions Authentication { get; set; }
}

public class BotAuthenticationOptions
{
    public string BotId { get; set; }
    public string MicrosoftAppId { get; set; }
    public string MicrosoftAppPassword { get; set; }
}
```

## CoreBotAuthententicationAttribute

```C#
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
```

## CoreDialogModule

The `CoreDialogModule` class encapsulates a set of Autofac registrations needed to provide the Bot Builder and Bot Connector with the credentials it needs.

```C#
public class CoreDialogModule : Module
 {
     protected override void Load(ContainerBuilder builder)
     {
         // Allow the filter to be resolved
         builder
             .RegisterType<CoreBotAuthententicationAttribute>()
             .InstancePerLifetimeScope();

         // Allow for BotId to be resolved from the options object
         builder.Register(ctx =>
         {
             var botOptions = ctx.Resolve<BotOptions>();
             return new Microsoft.Bot.Builder.Dialogs.Internals.BotIdResolver(botOptions?.Authentication?.MicrosoftAppId);
         })
         .AsImplementedInterfaces()
         .AsSelf()
         .SingleInstance();

         // Allow for credentials to come from Bot Options
         builder
             .Register(ctx =>
             {
                 var botOptions = ctx.Resolve<BotOptions>();
                 return new MicrosoftAppCredentials(botOptions.Authentication.MicrosoftAppId,
                     botOptions.Authentication.MicrosoftAppPassword);
             })
             .AsSelf()
             .SingleInstance();
     }
 }
```

## Startup Changes

Below are the changes to `Startup` to get this all working.

```C#
public class Startup
{
    public Startup(IHostingEnvironment env)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(env.ContentRootPath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
            .AddEnvironmentVariables();
        Configuration = builder.Build();
    }

    public IConfigurationRoot Configuration { get; }
    public IContainer ApplicationContainer { get; private set; }

    // This method gets called by the runtime. Use this method to add services to the container.
    public IServiceProvider ConfigureServices(IServiceCollection services)
    {
        // Add framework services.
        services
            .AddMvc()
        // The configuration below applies the JSON serialization settings the BOT framework expects
            .AddJsonOptions(options =>
            {
                options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
                options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
                options.SerializerSettings.Formatting = Formatting.Indented;
                JsonConvert.DefaultSettings = () => new JsonSerializerSettings()
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver(),
                    Formatting = Newtonsoft.Json.Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore,
                };
            });

        services.ConfigurePoco<BotOptions>(Configuration.GetSection("Bot"));

        var builder = new ContainerBuilder();
        builder.Populate(services);
        builder.RegisterModule(new ReflectionSurrogateModule());
        builder.RegisterModule(new CoreDialogModule());
        builder.Update(Conversation.Container);
        ApplicationContainer = Conversation.Container;
        return new AutofacServiceProvider(ApplicationContainer);
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory, IApplicationLifetime lifetime)
    {
        loggerFactory.AddConsole(Configuration.GetSection("Logging"));
        loggerFactory.AddDebug();

        // If you want to dispose of resources that have been resolved in the
        // application container, register for the "ApplicationStopped" event.
        lifetime.ApplicationStopped.Register(() => this.ApplicationContainer.Dispose());
        app.UseMvc();
    }
}
```
