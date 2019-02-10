namespace Oxide.Ext.Discord.WebSockets
{
    using Oxide.Ext.Discord.Exceptions;
    using System;
    using System.Threading;
    using WebSocketSharp;

    public class Socket
    {
        private readonly DiscordClient client;

        private readonly WebSocket socket;

        private SocketListner listner;

        private int reconnectAttempts = 0;

        public Socket(DiscordClient client)
        {
            this.client = client;
            socket = InitializeSocket();
        }

        protected internal WebSocket InitializeSocket()
        {
            WebSocket socket = new WebSocket(client.WSSURL);
            listner = new SocketListner(client, this);

            socket.OnOpen += listner.SocketOpened;
            socket.OnClose += listner.SocketClosed;
            socket.OnError += listner.SocketErrored;
            socket.OnMessage += listner.SocketMessage;

            return socket;
        }



        public object _lock = new object();

        internal void Reconnect()
        {
            if (IsClosed)
            {
                throw new SocketReconnectException();
            }

            int time = (reconnectAttempts > 100 ? 60 : 3) * 1000;

            reconnectAttempts++;

            Thread thread = new Thread(() =>
            {
                Thread.Sleep(time);
                Connect();
            });
            thread.Start();
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
            if (IsClosed)
            {
                return;
            }

            socket?.CloseAsync();
        }

        public void Send(string message, Action<bool> completed = null)
        {
            socket?.SendAsync(message, completed);
        }

        public bool IsAlive => socket?.IsAlive ?? false;

        public bool IsClosing => socket?.ReadyState == WebSocketState.Closing;

        public bool IsClosed => socket?.ReadyState == WebSocketState.Closed;
    }
}
