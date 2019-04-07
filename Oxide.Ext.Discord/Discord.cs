namespace Oxide.Ext.Discord
{
    using System.Collections.Generic;
    using System.Linq;
    using Oxide.Core.Plugins;
    using Oxide.Ext.Discord.Exceptions;

    public class Discord
    {
        private static List<DiscordClient> Clients { get; set; } = new List<DiscordClient>();

        public static DiscordClient[] GetClients { get => Clients.ToArray(); }

        public static void CreateClient(Plugin plugin, string apiKey)
        {
            if (plugin == null)
            {
                throw new PluginNullException();
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                throw new APIKeyException();
            }

            DiscordSettings settings = new DiscordSettings()
            {
                ApiToken = apiKey
            };

            CreateClient(plugin, settings);
        }

        public static void CreateClient(Plugin plugin, DiscordSettings settings)
        {
            if (plugin == null)
            {
                throw new PluginNullException();
            }
            
            if (settings == null)
            {
                throw new SettingsNullException();
            }

            if (string.IsNullOrEmpty(settings.ApiToken))
            {
                throw new APIKeyException();
            }

            // Find an existing DiscordClient and update it 
            var client = Clients.FirstOrDefault(x => x.Settings.ApiToken == settings.ApiToken);
            if (client != null)
            {
                client.Plugins.Where(x => x.Title == plugin.Title).ToList().ForEach(x => client.Plugins.Remove(x));

                client.Plugins.Remove(plugin);
                client.RegisterPlugin(plugin);
                client.UpdatePluginReference(plugin);
                client.Settings = settings;
                client.CallHook("DiscordSocket_Initialized", plugin);
                return;
            }

            // Create a new DiscordClient
            DiscordClient newClient = new DiscordClient();
            Clients.Add(newClient);
            newClient.Initialize(plugin, settings);
        }

        public static void CloseClient(DiscordClient client)
        {
            if (client == null) return;

            client.Disconnect();
            Clients.Remove(client);
        }
    }
}
