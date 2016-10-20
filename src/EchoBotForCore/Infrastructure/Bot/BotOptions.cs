namespace EchoBotForCore.Infrastructure.Bot
{
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
}