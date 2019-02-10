using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Oxide.Ext.Discord.Exceptions
{
    class ThreadRunningException : Exception
    {
        public ThreadRunningException(Thread thread) : base(string.Format("Tried to start already running thread ({0}) ", thread.Name));
    }
}
