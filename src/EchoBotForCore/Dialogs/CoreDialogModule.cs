using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using EchoBotForCore.Infrastructure.Bot;
using Microsoft.Bot.Connector;

namespace EchoBotForCore.Dialogs
{
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
}
