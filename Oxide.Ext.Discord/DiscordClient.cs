using System.Threading.Tasks;

namespace Oxide.Ext.Discord
{
    using Newtonsoft.Json;
    using Oxide.Core;
    using Oxide.Core.Plugins;
    using Oxide.Ext.Discord.Attributes;
    using Oxide.Ext.Discord.DiscordEvents;
    using Oxide.Ext.Discord.DiscordObjects;
    using Oxide.Ext.Discord.Exceptions;
    using Oxide.Ext.Discord.Gateway;
    using Oxide.Ext.Discord.Helpers;
    using Oxide.Ext.Discord.REST;
    using Oxide.Ext.Discord.WebSockets;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Threading;

    public class DiscordClient
    {
        public List<Plugin> Plugins { get; private set; } = new List<Plugin>();

        public RESTHandler REST { get; private set; }

        public string WSSURL { get; private set; }

        public DiscordSettings Settings { get; set; } = new DiscordSettings();

        public Guild DiscordServer { get; set; }

        public int Sequence;

        public string SessionId;

        private Socket _webSocket;

        private Thread _heartbeat;
        private Thread _runThread;

        private double _lastHeartbeat;

        public ClientState ClientState { get; private set; }

        public void Initialize(Plugin plugin, DiscordSettings settings, bool _override = false)
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

            _runThread = new Thread(new ThreadStart(() =>
            {
                RegisterPlugin(plugin);

                Settings = settings;

                REST = new RESTHandler(Settings.ApiToken);

                if (_webSocket != null && _webSocket.IsAlive)
                {
                    throw new SocketRunningException(this);
                }

                GetUrl();

                if (_webSocket == null)
                {
                    _webSocket = new Socket(this);
                }

                _webSocket.Connect();

            }));
        }

        public void Disconnect()
        {
            ClientState = ClientState.DISCONNECTED;
            _webSocket?.Disconnect();
            REST?.Shutdown();
            _heartbeat?.Abort();
            _runThread?.Abort();
        }
        public void Connect()
        {
            ClientState = ClientState.CONNECTED;
            _webSocket.Connect();
        }

        public void UpdatePluginReference(Plugin plugin = null)
        {
            List<Plugin> affectedPlugins = (plugin == null) ? Plugins : new List<Plugin>() { plugin };

            foreach (Plugin pluginItem in affectedPlugins)
            {
                foreach (FieldInfo field in pluginItem.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (field.GetCustomAttributes(typeof(DiscordClientAttribute), true).Any())
                    {
                        field.SetValue(pluginItem, this);
                    }
                }
            }
        }

        public void RegisterPlugin(Plugin plugin)
        {
            IEnumerable<Plugin> search = Plugins.Where(x => x.Title == plugin.Title);
            search.ToList().ForEach(x => Plugins.Remove(x));

            Plugins.Add(plugin);
        }

        public object CallHook(string hookname, params object[] args) => CallHook(hookname, null, args);

        public object CallHook(string hookname, Plugin specificPlugin = null, params object[] args)
        {
            if (specificPlugin != null)
            {
                if (!specificPlugin.IsLoaded)
                {
                    return null;
                }

                return specificPlugin.CallHook(hookname, args);
            }

            Dictionary<string, object> returnValues = new Dictionary<string, object>();

            foreach (Plugin plugin in Plugins.Where(x => x.IsLoaded))
            {
                object retVal = plugin.CallHook(hookname, args);
                returnValues.Add(plugin.Title, retVal);
            }

            if (returnValues.Count(x => x.Value != null) > 1)
            {
                string conflicts = string.Join("\n", returnValues.Select(x => $"Plugin {x.Key} - {x.Value}").ToArray());
                Interface.Oxide.LogWarning($"[Discord Ext] A hook conflict was triggered on {hookname} between:\n{conflicts}");
                return null;
            }

            return returnValues.FirstOrDefault(x => x.Value != null).Value;
        }

        public string GetPluginNames(string delimiter = ", ") => string.Join(delimiter, Plugins.Select(x => x.Name).ToArray());

        public void StopHeartbeatThread()
        {
            if (_heartbeat != null)
            {
                _heartbeat.Abort();
                _heartbeat = null;
            }
        }
        public void StartHeartbeatThread(int heartbeatInterval)
        {
            if (_heartbeat != null && _heartbeat.IsAlive)
            {
                throw new ThreadRunningException(_heartbeat);
            }
            _heartbeat = new Thread(new ThreadStart(() =>
            {
                while (true)
                {
                    Thread.Sleep(heartbeatInterval);
                    SendHeartbeat();
                }
            }))
            {
                Name = "Heartbeat-Thread"
            };
            _heartbeat.Start();
            if (Settings.Debugging)
            {
                Interface.Oxide.LogDebug(_heartbeat.Name);
            }
        }

        private void GetUrl()
        {
            var reset = new AutoResetEvent(false);
            DiscordObjects.Gateway.GetGateway(this, gateway =>
            {
                // Example: wss://gateway.discord.gg/?v=6&encoding=json
                var fullUrl = $"{gateway.URL}/?{Gateway.Connect.Serialize()}";

                if (Settings.Debugging)
                {
                    Interface.Oxide.LogDebug($"Got Gateway url: {fullUrl}");
                }

                WSSURL = fullUrl;
                reset.Set();
            });
            reset.WaitOne();
        }

        #region Discord Events

        public void Identify()
        {
            // Sent immediately after connecting. Opcode 2: Identify
            // Ref: https://discordapp.com/developers/docs/topics/gateway#identifying

            Identify identify = new Identify()
            {
                Token = Settings.ApiToken,
                Properties = new Properties()
                {
                    OS = "Oxide.Ext.Discord",
                    Browser = "Oxide.Ext.Discord",
                    Device = "Oxide.Ext.Discord"
                },
                Compress = false,
                LargeThreshold = 50,
                Shard = new List<int>() { 0, 1 }
            };

            SPayload<Identify> opcode = new SPayload<Identify>()
            {
                OpCode = OpCode.Identify,
                Data = identify
            };
            string payload = JsonConvert.SerializeObject(opcode);

            _webSocket.Send(payload);
        }

        // TODO: Implement the usage of this event
        public void Resume()
        {
            Resume resume = new Resume()
            {
                Sequence = Sequence,
                SessionID = SessionId,
                Token = string.Empty // What is this meant to be?
            };

            SPayload<Resume> packet = new SPayload<Resume>()
            {
                OpCode = OpCode.Resume,
                Data = resume
            };

            string payload = JsonConvert.SerializeObject(packet);
            _webSocket.Send(payload);
        }

        internal void SendHeartbeat()
        {
            SPayload<double> packet = new SPayload<double>()
            {
                OpCode = OpCode.Heartbeat,
                Data = _lastHeartbeat
            };

            string message = JsonConvert.SerializeObject(packet);
            _webSocket.Send(message);

            _lastHeartbeat = Time.TimeSinceEpoch();

            CallHook("DiscordSocket_HeartbeatSent");

            if (Settings.Debugging)
            {
                Interface.Oxide.LogDebug($"Heartbeat sent!");
            }
        }

        public void RequestGuildMembers(string query = "", int limit = 0)
        {
            GuildMembersRequest requestGuildMembers = new GuildMembersRequest()
            {
                GuildID = DiscordServer.id,
                Query = query,
                Limit = limit
            };

            SPayload<GuildMembersRequest> packet = new SPayload<GuildMembersRequest>()
            {
                OpCode = OpCode.RequestGuildMembers,
                Data = requestGuildMembers
            };

            string payload = JsonConvert.SerializeObject(packet);
            _webSocket.Send(payload);
        }

        public void UpdateVoiceState(string channelId, bool selfDeaf, bool selfMute)
        {
            VoiceStateUpdate voiceState = new VoiceStateUpdate()
            {
                ChannelID = channelId,
                GuildID = DiscordServer.id,
                SelfDeaf = selfDeaf,
                SelfMute = selfMute
            };

            SPayload<VoiceStateUpdate> packet = new SPayload<VoiceStateUpdate>()
            {
                OpCode = OpCode.VoiceStateUpdate,
                Data = voiceState
            };

            string payload = JsonConvert.SerializeObject(packet);
            _webSocket.Send(payload);
        }

        public void UpdateStatus(Presence presence)
        {
            SPayload<Presence> opcode = new SPayload<Presence>()
            {
                OpCode = OpCode.StatusUpdate,
                Data = presence
            };

            string payload = JsonConvert.SerializeObject(opcode);
            _webSocket.Send(payload);
        }

        #endregion
    }
}
