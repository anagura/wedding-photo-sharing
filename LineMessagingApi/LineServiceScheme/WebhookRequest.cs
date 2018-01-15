using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace LineMessagingApi
{
    public class WebhookRequest
    {
        [DataMember(Name = "events")]
        public WebhookRequestEvent[] Events { get; set; }
    }

    public class WebhookRequestEvent
    {
        [DataMember(Name = "replyToken")]
        public string ReplyToken { get; set; }


    }
}
