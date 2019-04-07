using System.Security.Cryptography.X509Certificates;

namespace Oxide.Ext.Discord.WebSockets
{
    using Exceptions;
    using System;
    using System.Threading;
    using WebSocketSharp;

    public class Socket
    {
        private readonly DiscordClient _client;

        private readonly WebSocket _socket;

        private SocketListener _listener;

        private int _reconnectAttempts;

        private Thread _connectionThread;

        public int SleepTime;

        public Socket(DiscordClient client)
        {
            _client = client;
            _socket = InitializeSocket();
        }

        private WebSocket InitializeSocket()
        {
            var socket = new WebSocket(_client.WebSocketUrl);
            _listener = new SocketListener(_client, this);

            socket.OnOpen += _listener.SocketOpened;
            socket.OnClose += _listener.SocketClosed;
            socket.OnError += _listener.SocketError;
            socket.OnMessage += _listener.SocketMessage;

            return socket;
        }

        internal void Reconnect()
        {
            Disconnect();

            if (_socket.ReadyState == WebSocketState.Closed)
            {
                throw new SocketReconnectException();
            }

            SleepTime = (_reconnectAttempts > 100 ? 60 : 3) * 1000;

            _reconnectAttempts++;

            Connect();
        }

        public void Connect()
        {
            if (_connectionThread != null && _connectionThread.IsAlive)
            {
                throw new Exception("Connect thread already alive!");
            }
            if (_socket?.ReadyState == WebSocketState.Open)
            {
                throw new SocketRunningException(_client);
            }
            _connectionThread = new Thread(OpenSocket);
        }

        private void OpenSocket()
        {
            if (SleepTime > 0)
            {
                Thread.Sleep(SleepTime);
            }
            _socket.Connect();
        }

        public void Disconnect()
        {
            if (IsClosed)
            {
                return;
            }
            _client.StopHeartbeatThread();
            _socket?.Close();
        }

        public void Shutdown()
        {
            Disconnect();
        }

        public void Send(string message, Action<bool> completed = null)
        {
            _socket?.SendAsync(message, completed);
        }

        public bool IsAlive => _socket?.IsAlive ?? false;

        public bool IsClosing => _socket?.ReadyState == WebSocketState.Closing;

        public bool IsClosed => _socket?.ReadyState == WebSocketState.Closed;
    }
}
