namespace Oxide.Ext.Discord
{
    using Oxide.Core;
    using Oxide.Core.Extensions;
    using Oxide.Core.Plugins;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    public class DiscordExtension : Extension
    {
        internal static readonly Version AssemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;

        public DiscordExtension(ExtensionManager manager) : base(manager)
        {
        }

        ////public override bool SupportsReloading => true;

        public override string Name => "Discord";

        public override string Author => "PsychoTea & DylanSMR";

        public override VersionNumber Version => new VersionNumber(AssemblyVersion.Major, AssemblyVersion.Minor, AssemblyVersion.Build);

        public override void OnModLoad()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, exception) =>
            {
                Interface.Oxide.LogException("An exception was thrown!", exception.ExceptionObject as Exception);
            };
        }

        public override void OnShutdown()
        {
            // new List prevents against InvalidOperationException
            foreach (DiscordClient client in new List<DiscordClient>(Discord.GetClients))
            {
                Discord.CloseClient(client);
            }

            Interface.Oxide.LogInfo("[Discord Ext] Disconnected all clients - server shutdown.");
        }

        [HookMethod("OnPluginUnloaded")]
        private void OnPluginUnloaded(Plugin plugin)
        {
            Console.WriteLine("Test");
            foreach (DiscordClient client in Discord.GetClients)
            {
                if (client.Plugins.Count != 1 && !client.Plugins.Any(x => x.Name == plugin.Name || x.Filename == plugin.Filename)) continue;
                Interface.Oxide.LogInfo("Disconnecting: Plugin \"{0}\" unloaded", plugin.Name);
                client.Disconnect();
            }
        }
    }
}
