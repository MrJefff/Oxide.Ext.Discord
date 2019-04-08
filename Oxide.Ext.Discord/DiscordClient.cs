using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
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

namespace Oxide.Ext.Discord
{
    public class DiscordClient
    {
        private readonly List<Plugin> _plugins = new List<Plugin>();
        private Dictionary<string, Guild> _guilds = new Dictionary<string, Guild>();
        public IEnumerable<Guild> Guilds => _guilds.Values;
        public IEnumerable<Plugin> Plugins => _plugins;

        public RESTHandler Rest { get; private set; }

        public string WebSocketUrl { get; private set; }

        public DiscordSettings Settings { get; set; } = new DiscordSettings();

        public int Sequence;

        private string _sessionId;

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

            _runThread = new Thread(() =>
            {
                RegisterPlugin(plugin);

                Settings = settings;

                Rest = new RESTHandler(Settings.ApiToken);

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

            });
            _runThread.Start();
        }

        public void Ready(Ready ready)
        {
            if (!_webSocket.Initialized)
            {
                _guilds.Clear();

                foreach (var guild in ready.Guilds)
                {
                    _guilds.Add(guild.id, guild);
                }
                _sessionId = ready.SessionID;
                _webSocket.ShouldResume = true;
            }

            _webSocket.Initialized = true;
        }

        internal void RemovePlugin(Plugin plugin)
        {
            _plugins.Remove(plugin);
        }

        internal void AddGuild(Guild guild)
        {
            _guilds.Add(guild.id, guild);
        }

        internal void AddChannel(Channel channel)
        {
            if (string.IsNullOrEmpty(channel.GuildId))
            {
                return;
            }
            _guilds[channel.GuildId].channels.Add(channel);
        }
        internal void RemoveChannel(Channel channel)
        {
            if (string.IsNullOrEmpty(channel.GuildId))
            {
                return;
            }
            _guilds[channel.GuildId].channels.Remove(channel);
        }
        internal Channel UpdateChannel(Channel channel)
        {
            if (string.IsNullOrEmpty(channel.GuildId))
            {
                return null;
            }
            var channelPrevious = _guilds[channel.GuildId].channels.FirstOrDefault(x => x.id == channel.id);

            if (channelPrevious != null)
            {
                RemoveChannel(channelPrevious);
            }

            AddChannel(channel);
            return channelPrevious;
        }

        internal void AddMember(GuildMemberAdd member)
        {
            if (string.IsNullOrEmpty(member.guild_id))
            {
                return;
            }
            _guilds[member.guild_id].members.Add(member);
        }

        internal void RemoveMember(GuildMemberRemove member)
        {
            if (string.IsNullOrEmpty(member.guild_id))
            {
                return;
            }
            _guilds[member.guild_id].members.Remove(member);
        }
        internal GuildMember UpdateMember(GuildMemberUpdate member)
        {
            if (string.IsNullOrEmpty(member.guild_id))
            {
                return null;
            }

            var previousMember = _guilds[member.guild_id].members.First(x => x.user.id == member.user.id);
            if (previousMember != null)
            {
                _guilds[member.guild_id].members.Remove(previousMember);
            }
            _guilds[member.guild_id].members.Add(member);
            return previousMember;
        }

        internal void AddRole(GuildRoleCreate role)
        {
            if (string.IsNullOrEmpty(role.guild_id))
            {
                return;
            }
            _guilds[role.guild_id].roles.Add(role.role);
        }

        internal Role RemoveRole(GuildRoleDelete role)
        {
            if (string.IsNullOrEmpty(role.guild_id))
            {
                return null;
            }
            var removeRole = _guilds[role.guild_id].roles.First(x => x.id == role.role_id);
            if (removeRole != null)
            {
                _guilds[role.guild_id].roles.Remove(removeRole);
                return removeRole;
            }
            else
            {
                return null;
            }
        }
        internal Role UpdateRole(GuildRoleUpdate role)
        {
            if (string.IsNullOrEmpty(role.guild_id))
            {
                return null;
            }

            var previousRole = _guilds[role.guild_id].roles.First(x => x.id == role.role.id);
            if (previousRole != null)
            {
                _guilds[previousRole.guild_id].roles.Remove(previousRole);
            }
            _guilds[role.guild_id].roles.Add(role.role);
            return previousRole;
        }

        internal void UpdateUserPresence(PresenceUpdate update)
        {
            var previousMember = _guilds[update.guild_id].members.First(x => x.user.id == update.user.id);
            if (previousMember != null)
            {
                previousMember.user = update.user;
            }
        }
        internal void UpdateUser(User user)
        {
            foreach (var guild in _guilds.Values)
            {
                var member = guild.members.First(x => x.user.id == user.id);
                if (member != null)
                {
                    member.user = user;
                }
            }
        }


        public void Disconnect()
        {
            ClientState = ClientState.DISCONNECTED;
            _webSocket?.Disconnect();
            Rest?.Shutdown();
            _heartbeat?.Abort();
            _runThread?.Abort();
        }

        public void UpdatePluginReference(Plugin plugin = null)
        {
            var affectedPlugins = plugin == null ? _plugins : new List<Plugin> { plugin };

            foreach (var pluginItem in affectedPlugins)
            {
                foreach (var field in pluginItem.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
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
            var search = Plugins.Where(x => x.Title == plugin.Title);
            search.ToList().ForEach(x => _plugins.Remove(x));

            _plugins.Add(plugin);
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
            _heartbeat = new Thread(() =>
            {
                while (_webSocket.IsAlive)
                {
                    Thread.Sleep(heartbeatInterval);
                    SendHeartbeat();
                }
            })
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
                var fullUrl = $"{gateway.URL}/?{Connect.Serialize()}";

                if (Settings.Debugging)
                {
                    Interface.Oxide.LogDebug($"Got Gateway url: {fullUrl}");
                }

                WebSocketUrl = fullUrl;
                reset.Set();
            });
            reset.WaitOne();
        }

        #region Discord Events

        public void Identify()
        {
            // Sent immediately after connecting. Opcode 2: Identify
            // Ref: https://discordapp.com/developers/docs/topics/gateway#identifying

            var identify = new Identify
            {
                Token = Settings.ApiToken,
                Properties = new Properties
                {
                    OS = "Oxide.Ext.Discord",
                    Browser = "Oxide.Ext.Discord",
                    Device = "Oxide.Ext.Discord"
                },
                Compress = false,
                LargeThreshold = 50,
                Shard = new List<int> { 0, 1 }
            };

            var opcode = new SPayload<Identify>
            {
                OpCode = OpCode.Identify,
                Data = identify
            };

            var payload = JsonConvert.SerializeObject(opcode);

            _webSocket.Send(payload);
        }

        // TODO: Implement the usage of this event
        public void Resume()
        {
            var resume = new Resume
            {
                Sequence = Sequence,
                SessionID = _sessionId,
                Token = Settings.ApiToken
            };

            var packet = new SPayload<Resume>
            {
                OpCode = OpCode.Resume,
                Data = resume
            };

            string payload = JsonConvert.SerializeObject(packet);
            _webSocket.Send(payload);
        }

        internal void SendHeartbeat()
        {
            SPayload<double> packet = new SPayload<double>
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
                Interface.Oxide.LogDebug("Heartbeat sent!");
            }
        }

        public void RequestGuildMembers(string guildId, string query = "", int limit = 0)
        {
            GuildMembersRequest requestGuildMembers = new GuildMembersRequest
            {
                GuildID = guildId,
                Query = query,
                Limit = limit
            };

            SPayload<GuildMembersRequest> packet = new SPayload<GuildMembersRequest>
            {
                OpCode = OpCode.RequestGuildMembers,
                Data = requestGuildMembers
            };

            string payload = JsonConvert.SerializeObject(packet);
            _webSocket.Send(payload);
        }

        public void UpdateVoiceState(string guildId, string channelId, bool selfDeaf, bool selfMute)
        {
            VoiceStateUpdate voiceState = new VoiceStateUpdate
            {
                ChannelID = channelId,
                GuildID = guildId,
                SelfDeaf = selfDeaf,
                SelfMute = selfMute
            };

            SPayload<VoiceStateUpdate> packet = new SPayload<VoiceStateUpdate>
            {
                OpCode = OpCode.VoiceStateUpdate,
                Data = voiceState
            };

            string payload = JsonConvert.SerializeObject(packet);
            _webSocket.Send(payload);
        }

        public void UpdateStatus(Presence presence)
        {
            SPayload<Presence> opcode = new SPayload<Presence>
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
