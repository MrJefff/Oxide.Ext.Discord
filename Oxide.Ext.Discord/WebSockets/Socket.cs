namespace Oxide.Ext.Discord.WebSockets
{
    using System;
    using System.Threading;
    using Oxide.Ext.Discord.Exceptions;
    using WebSocketSharp;

    public class Socket
    {
        private readonly DiscordClient client;

        private readonly WebSocket socket;

        private readonly SocketListner listner;

        public Socket(DiscordClient client)
        {
            this.client = client;
            socket = new WebSocket(client.WSSURL);
            listner = new SocketListner(client, this);

            socket.OnOpen += listner.SocketOpened;
            socket.OnClose += listner.SocketClosed;
            socket.OnError += listner.SocketErrored;
            socket.OnMessage += listner.SocketMessage;
            reconenctThread = new Thread(Reconnect);
        }

        public Thread reconenctThread;


        public object _lock = new object();

        internal void Reconnect()
        {
            Console.WriteLine("Reconnect!");
            lock (_lock)
            Thread.Sleep(3000);
            Connect();
            reconenctThread.Abort();
        }

        public void Connect()
        {
            if (socket?.ReadyState == WebSocketState.Open)
            {
                throw new SocketRunningException(client);
            }
            socket.ConnectAsync();
        }

        public void Disconnect()
        {
            if (IsClosed) return;

            socket?.CloseAsync();
        }

        public void Send(string message, Action<bool> completed = null) => socket?.SendAsync(message, completed);

        public bool IsAlive
        {
            get => socket?.IsAlive ?? false;
        }

        public bool IsClosing
        {
            get => socket?.ReadyState == WebSocketState.Closing;
        }

        public bool IsClosed
        {
            get => socket?.ReadyState == WebSocketState.Closed;
        }
    }
}
