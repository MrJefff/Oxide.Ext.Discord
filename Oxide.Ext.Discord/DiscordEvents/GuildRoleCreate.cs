namespace Oxide.Ext.Discord.DiscordEvents
{
    using DiscordObjects;

    public class GuildRoleCreate
    {
        public string guild_id { get; set; }
        public Role role { get; set; }

    }
}
