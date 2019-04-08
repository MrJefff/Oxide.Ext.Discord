using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Ext.Discord.DiscordEvents;
using Oxide.Ext.Discord.DiscordObjects;
using Oxide.Ext.Discord.Exceptions;
using Oxide.Ext.Discord.Gateway;
using System;
using WebSocketSharp;

namespace Oxide.Ext.Discord.WebSockets
{
    public class SocketListener
    {
        private readonly DiscordClient _client;

        private readonly Socket _webSocket;

        public SocketListener(DiscordClient client, Socket socket)
        {
            _client = client;
            _webSocket = socket;
        }

        public void SocketOpened(object sender, EventArgs e)
        {
            if (_client.Settings.Debugging)
            {
                Interface.Oxide.LogDebug("Discord WebSocket opened.");
            }

            _webSocket.SleepTime = 0;

            _client.CallHook("DiscordSocket_WebSocketOpened");
        }

        public void SocketClosed(object sender, CloseEventArgs e)
        {
            if (e.Code == 4004)
            {
                _client.Disconnect();
                throw new APIKeyException();
            }

            if (_client.Settings.Debugging)
            {
                Interface.Oxide.LogDebug($"Discord WebSocket closed. Code: {e.Code}, reason: {e.Reason}");
            }

            if (!e.WasClean)
            {
                Interface.Oxide.LogWarning($"[Discord Ext] Discord connection closed uncleanly: code {e.Code}, Reason: {e.Reason}");

                Interface.Oxide.LogWarning("[Discord Ext] Attempting to reconnect to Discord...");

            }

            _webSocket.Initialized = false;

            if (!Interface.Oxide.IsShuttingDown && _client.ClientState != ClientState.DISCONNECTED)
            {
                _webSocket.Reconnect();
            }
            else
            {
                _client.Disconnect();
                Discord.CloseClient(_client);
            }


            _client.CallHook("DiscordSocket_WebSocketClosed", null, e.Reason, e.Code, e.WasClean);
        }

        public void SocketError(object sender, ErrorEventArgs e)
        {
            Interface.Oxide.LogWarning($"[Discord Ext] An error has occured: Response: {e.Message}");

            _client.CallHook("DiscordSocket_WebSocketErrored", null, e.Exception, e.Message);
        }

        public void SocketMessage(object sender, MessageEventArgs e)
        {
            RPayload payload = JsonConvert.DeserializeObject<RPayload>(e.Data);

            if (payload.Sequence.HasValue)
            {
                _client.Sequence = payload.Sequence.Value;
            }

            if (_client.Settings.Debugging)
            {
                Interface.Oxide.LogDebug($"Received socket message, OpCode: {payload.OpCode}");
            }

            switch (payload.OpCode)
            {
                // Dispatch (dispatches an event)
                case OpCode.Dispatch:
                    {
                        if (_client.Settings.Debugging)
                        {
                            Interface.Oxide.LogDebug($"Received OpCode 0, event: {payload.EventName}");
                        }

                        switch (payload.EventName)
                        {
                            case "READY":
                                {
                                    _client.UpdatePluginReference();
                                    _client.CallHook("DiscordSocket_Initialized");

                                    var ready = payload.ToObject<Ready>();

                                    if (ready.Guilds.Count == 0 &&
                                        _client.Settings.Debugging)
                                    {
                                        Interface.Oxide.LogDebug("Ready event but no Guilds sent.");
                                    }

                                    _client.Ready(ready);

                                    _client.CallHook("Discord_Ready", null, ready);
                                    break;
                                }

                            case "RESUMED":
                                {
                                    Resumed resumed = payload.ToObject<Resumed>();
                                    _client.CallHook("Discord_Resumed", null, resumed);
                                    break;
                                }

                            case "CHANNEL_CREATE":
                                {
                                    Channel channelCreate = payload.ToObject<Channel>();
                                    _client.AddChannel(channelCreate);
                                    _client.CallHook("Discord_ChannelCreate", null, channelCreate);
                                    break;
                                }

                            case "CHANNEL_UPDATE":
                                {
                                    Channel channelUpdated = payload.ToObject<Channel>();

                                    var prev = _client.UpdateChannel(channelUpdated);

                                    _client.CallHook("Discord_ChannelUpdate", null, channelUpdated, prev);
                                    break;
                                }

                            case "CHANNEL_DELETE":
                                {
                                    Channel channelDelete = payload.ToObject<Channel>();

                                    _client.RemoveChannel(channelDelete);

                                    _client.CallHook("Discord_ChannelDelete", null, channelDelete);
                                    break;
                                }

                            case "CHANNEL_PINS_UPDATE":
                                {
                                    ChannelPinsUpdate channelPinsUpdate = payload.ToObject<ChannelPinsUpdate>();
                                    _client.CallHook("Discord_ChannelPinsUpdate", null, channelPinsUpdate);
                                    break;
                                }

                            // this isn't set up right
                            // https://discordapp.com/developers/docs/topics/gateway#guild-create
                            case "GUILD_CREATE":
                                {
                                    var guildCreate = payload.ToObject<Guild>();
                                    _client.AddGuild(guildCreate);
                                    _client.CallHook("Discord_GuildCreate", null, guildCreate);
                                    break;
                                }

                            case "GUILD_UPDATE":
                                {
                                    Guild guildUpdate = payload.ToObject<Guild>();
                                    _client.CallHook("Discord_GuildUpdate", null, guildUpdate);
                                    break;
                                }

                            case "GUILD_DELETE":
                                {
                                    Guild guildDelete = payload.ToObject<Guild>();
                                    _client.CallHook("Discord_GuildDelete", null, guildDelete);
                                    break;
                                }

                            case "GUILD_BAN_ADD":
                                {
                                    BanObject ban = payload.ToObject<BanObject>();
                                    _client.CallHook("Discord_GuildBanAdd", null, ban);
                                    break;
                                }

                            case "GUILD_BAN_REMOVE":
                                {
                                    BanObject unban = payload.ToObject<BanObject>();
                                    _client.CallHook("Discord_GuildBanRemove", null, unban);
                                    break;
                                }

                            case "GUILD_EMOJIS_UPDATE":
                                {
                                    GuildEmojisUpdate guildEmojisUpdate = payload.ToObject<GuildEmojisUpdate>();
                                    _client.CallHook("Discord_GuildEmojisUpdate", null, guildEmojisUpdate);
                                    break;
                                }

                            case "GUILD_INTEGRATIONS_UPDATE":
                                {
                                    var guildIntergrationsUpdate = payload.ToObject<GuildIntergrationsUpdate>();
                                    _client.CallHook("Discord_GuildIntergrationsUpdate", null, guildIntergrationsUpdate);
                                    break;
                                }

                            case "GUILD_MEMBER_ADD":
                                {
                                    var memberAdded = payload.ToObject<GuildMemberAdd>();

                                    _client.AddMember(memberAdded);

                                    _client.CallHook("Discord_MemberAdded", null, memberAdded);
                                    break;
                                }

                            case "GUILD_MEMBER_REMOVE":
                                {
                                    var memberRemoved = payload.ToObject<GuildMemberRemove>();

                                    _client.RemoveMember(memberRemoved);

                                    _client.CallHook("Discord_MemberRemoved", null, memberRemoved);
                                    break;
                                }

                            case "GUILD_MEMBER_UPDATE":
                                {
                                    var memberUpdated = payload.ToObject<GuildMemberUpdate>();

                                    var prev = _client.UpdateMember(memberUpdated);

                                    _client.CallHook("Discord_GuildMemberUpdate", null, memberUpdated, prev);
                                    break;
                                }

                            case "GUILD_MEMBERS_CHUNK":
                                {
                                    var guildMembersChunk = payload.ToObject<GuildMembersChunk>();
                                    _client.CallHook("Discord_GuildMembersChunk", null, guildMembersChunk);
                                    break;
                                }

                            case "GUILD_ROLE_CREATE":
                                {
                                    var guildRoleCreate = payload.ToObject<GuildRoleCreate>();

                                    _client.AddRole(guildRoleCreate);

                                    _client.CallHook("Discord_GuildRoleCreate", null, guildRoleCreate);
                                    break;
                                }

                            case "GUILD_ROLE_UPDATE":
                                {
                                    var guildRoleUpdate = payload.ToObject<GuildRoleUpdate>();

                                    var prev = _client.UpdateRole(guildRoleUpdate);

                                    _client.CallHook("Discord_GuildRoleUpdate", null, guildRoleUpdate.role, prev);
                                    break;
                                }

                            case "GUILD_ROLE_DELETE":
                                {
                                    var guildRoleDelete = payload.ToObject<GuildRoleDelete>();

                                    var role =_client.RemoveRole(guildRoleDelete);

                                    _client.CallHook("Discord_GuildRoleDelete", null, role);
                                    break;
                                }

                            case "MESSAGE_CREATE":
                                {
                                    Message messageCreate = payload.ToObject<Message>();
                                    _client.CallHook("Discord_MessageCreate", null, messageCreate);
                                    break;
                                }

                            case "MESSAGE_UPDATE":
                                {
                                    Message messageUpdate = payload.ToObject<Message>();
                                    _client.CallHook("Discord_MessageUpdate", null, messageUpdate);
                                    break;
                                }

                            case "MESSAGE_DELETE":
                                {
                                    MessageDelete messageDelete = payload.ToObject<MessageDelete>();
                                    _client.CallHook("Discord_MessageDelete", null, messageDelete);
                                    break;
                                }

                            case "MESSAGE_DELETE_BULK":
                                {
                                    MessageDeleteBulk messageDeleteBulk = payload.ToObject<MessageDeleteBulk>();
                                    _client.CallHook("Discord_MessageDeleteBulk", null, messageDeleteBulk);
                                    break;
                                }

                            case "MESSAGE_REACTION_ADD":
                                {
                                    MessageReactionUpdate messageReactionAdd = payload.ToObject<MessageReactionUpdate>();
                                    _client.CallHook("Discord_MessageReactionAdd", null, messageReactionAdd);
                                    break;
                                }

                            case "MESSAGE_REACTION_REMOVE":
                                {
                                    MessageReactionUpdate messageReactionRemove = payload.ToObject<MessageReactionUpdate>();
                                    _client.CallHook("Discord_MessageReactionRemove", null, messageReactionRemove);
                                    break;
                                }

                            case "MESSAGE_REACTION_REMOVE_ALL":
                                {
                                    MessageReactionRemoveAll messageReactionRemoveAll = payload.ToObject<MessageReactionRemoveAll>();
                                    _client.CallHook("Discord_MessageReactionRemoveAll", null, messageReactionRemoveAll);
                                    break;
                                }

                            case "PRESENCE_UPDATE":
                                {
                                    var presenceUpdate = payload.ToObject<PresenceUpdate>();

                                    _client.UpdateUserPresence(presenceUpdate);

                                    _client.CallHook("Discord_PresenceUpdate", null, presenceUpdate?.user);
                                    break;
                                }

                            case "TYPING_START":
                                {
                                    var typingStart = payload.ToObject<TypingStart>();
                                    _client.CallHook("Discord_TypingStart", null, typingStart);
                                    break;
                                }

                            case "USER_UPDATE":
                                {
                                    var userUpdate = payload.ToObject<User>();

                                    _client.UpdateUser(userUpdate);

                                    _client.CallHook("Discord_UserUpdate", null, userUpdate);
                                    break;
                                }

                            case "VOICE_STATE_UPDATE":
                                {
                                    VoiceState voiceStateUpdate = payload.ToObject<VoiceState>();
                                    _client.CallHook("Discord_VoiceStateUpdate", null, voiceStateUpdate);
                                    break;
                                }

                            case "VOICE_SERVER_UPDATE":
                                {
                                    VoiceServerUpdate voiceServerUpdate = payload.ToObject<VoiceServerUpdate>();
                                    _client.CallHook("Discord_VoiceServerUpdate", null, voiceServerUpdate);
                                    break;
                                }

                            case "WEBHOOKS_UPDATE":
                                {
                                    WebhooksUpdate webhooksUpdate = payload.ToObject<WebhooksUpdate>();
                                    _client.CallHook("Discord_WebhooksUpdate", null, webhooksUpdate);
                                    break;
                                }

                            default:
                                {
                                    _client.CallHook("Discord_UnhandledEvent", null, payload);
                                    Interface.Oxide.LogWarning($"[Discord Ext] [Debug] Unhandled event: {payload.EventName}");
                                    break;
                                }
                        }

                        break;
                    }

                // Heartbeat
                // https://discordapp.com/developers/docs/topics/gateway#gateway-heartbeat
                case OpCode.Heartbeat:
                    {
                        Interface.Oxide.LogInfo("[DiscordExt] Manully sent heartbeat (received opcode 1)");
                        _client.SendHeartbeat();
                        break;
                    }

                // Reconnect (used to tell clients to reconnect to the gateway)
                // we should immediately reconnect here
                case OpCode.Reconnect:
                    {
                        Interface.Oxide.LogInfo("[DiscordExt] Reconnect has been called (opcode 7)! Reconnecting...");

                        _webSocket.Connect();
                        break;
                    }

                // Invalid Session (used to notify client they have an invalid session ID)
                case OpCode.InvalidSession:
                    {
                        Interface.Oxide.LogInfo("[DiscordExt] Invalid Session ID opcode recieved!");
                        _webSocket.ShouldResume = false;
                        break;
                    }

                // Hello (sent immediately after connecting, contains heartbeat and server debug information)
                case OpCode.Hello:
                    {
                        Hello hello = payload.ToObject<Hello>();
                        _client.StartHeartbeatThread(hello.HeartbeatInterval);

                        if (_webSocket.ShouldResume)
                        {
                            _client.Resume();
                        }
                        else
                        {
                            _client.Identify();
                        }
                        break;
                    }

                // Heartbeat ACK (sent immediately following a client heartbeat
                // that was received)
                // This should be changed: https://discordapp.com/developers/docs/topics/gateway#heartbeating
                // (See 'zombied or failed connections')
                case OpCode.HeartbeatACK:
                    {
                        break;
                    }

                default:
                    {
                        Interface.Oxide.LogInfo($"[DiscordExt] Unhandled OP code: code {payload.OpCode}");
                        break;
                    }
            }
        }
    }
}
