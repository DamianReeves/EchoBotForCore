using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using EchoBotForCore.Dialogs;
using EchoBotForCore.Infrastructure.Bot;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;

namespace EchoBotForCore.Controllers
{
    [Route("api/[controller]")]
    // Add a ServiceFilter to allow the CoreBotAuthenticationAttribute to get values via DI
    [ServiceFilter(typeof(CoreBotAuthententicationAttribute))]
    public class MessagesController : Controller
    {        
        public MessagesController(BotOptions botOptions)
        {
            if (botOptions == null) throw new ArgumentNullException(nameof(botOptions));
            BotOptions = botOptions;
        }

        public BotOptions BotOptions { get; }

        /// <summary>
        /// POST: api/Messages
        /// receive a message from a user and send replies
        /// </summary>
        /// <param name="activity"></param>
        [HttpPost]
        public virtual async Task<HttpResponseMessage> Post([FromBody] Activity activity)
        {
            // Get the conversation id so the bot answers.
            var conversationId = activity?.From?.Id;


            //// Get a valid token 
            //string token = await this.GetBotApiToken();

            //this.HttpContext.Response.Headers[HeaderNames.Authorization] = new AuthenticationHeaderValue("Bearer", token).ToString();

            // check if activity is of type message
            if (activity != null && activity.GetActivityType() == ActivityTypes.Message)
            {                
                using (var connector = new ConnectorClient(new Uri(activity.ServiceUrl)
                    , BotOptions.Authentication.MicrosoftAppId
                    , BotOptions.Authentication.MicrosoftAppPassword))
                {
                    // This show's usage using the ConnectorClient directly
                    var reply = activity.CreateReply($"[Using Connector Client] You said: {activity.Text}");
                    await connector.Conversations.ReplyToActivityAsync(reply);

                }

                await Conversation.SendAsync(activity, () => new EchoDialog());
            }
            else
            {
                HandleSystemMessage(activity);
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.Accepted);
        }

        private Activity HandleSystemMessage(Activity message)
        {
            if (message.Type == ActivityTypes.DeleteUserData)
            {
                // Implement user deletion here
                // If we handle user deletion, return a real message
            }
            else if (message.Type == ActivityTypes.ConversationUpdate)
            {
                // Handle conversation state changes, like members being added and removed
                // Use Activity.MembersAdded and Activity.MembersRemoved and Activity.Action for info
                // Not available in all channels
            }
            else if (message.Type == ActivityTypes.ContactRelationUpdate)
            {
                // Handle add/remove from contact lists
                // Activity.From + Activity.Action represent what happened
            }
            else if (message.Type == ActivityTypes.Typing)
            {
                // Handle knowing tha the user is typing
            }
            else if (message.Type == ActivityTypes.Ping)
            {
            }

            return null;
        }
    }
}
