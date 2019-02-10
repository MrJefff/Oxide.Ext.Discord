using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Oxide.Ext.Discord.Exceptions
{
    class SocketReconnectException : Exception
    {
        public SocketReconnectException() : base("Attempted to reconnect to unclosed socket!")
        {

        }
    }
}
