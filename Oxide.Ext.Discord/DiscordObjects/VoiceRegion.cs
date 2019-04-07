﻿namespace Oxide.Ext.Discord.DiscordObjects
{
    using System;
    using System.Collections.Generic;
    using Oxide.Ext.Discord.REST;

    public class VoiceRegion
    {
        public string id { get; set; }

        public string name { get; set; }

        public string sample_hostname { get; set; }

        public int? sample_port { get; set; }

        public bool? vip { get; set; }

        public bool? optimal { get; set; }

        public bool? deprecated { get; set; }

        public bool? custom { get; set; }

        public static void ListVoiceRegions(DiscordClient client, Action<List<VoiceRegion>> callback = null)
        {
            client.Rest.DoRequest($"/voice/regions", RequestMethod.GET, null, callback);
        }
    }
}
