namespace Oxide.Ext.Discord.Exceptions
{
    using System;

    public class NoURLException : Exception
    {
        public NoURLException() : base("Error! No WebSocketUrl was found.")
        {
        }
    }
}
