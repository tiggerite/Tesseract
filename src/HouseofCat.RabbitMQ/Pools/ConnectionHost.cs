using HouseofCat.Logger;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace HouseofCat.RabbitMQ.Pools
{
    public interface IConnectionHost
    {
        IConnection Connection { get; }
        ulong ConnectionId { get; }

        bool Blocked { get; }
        bool Dead { get; }
        bool Closed { get; }

        void AssignConnection(IConnection connection);
        void Close();
        Task<bool> HealthyAsync();
    }

    public class ConnectionHost : IConnectionHost, IDisposable
    {
        public IConnection Connection { get; private set; }
        public ulong ConnectionId { get; }

        public bool Blocked { get; private set; }
        public bool Dead { get; private set; }
        public bool Closed { get; private set; }

        private readonly ILogger<ConnectionHost> _logger;
        private readonly SemaphoreSlim _hostLock = new SemaphoreSlim(1, 1);
        private bool _disposedValue;

        public ConnectionHost(ulong connectionId, IConnection connection)
        {
            _logger = LogHelper.GetLogger<ConnectionHost>();
            ConnectionId = connectionId;

            AssignConnection(connection);
        }

        public void AssignConnection(IConnection connection)
        {
            EnterLock();

            if (Connection != null)
            {
                RemoveEventHandlers(Connection);

                try
                { Close(); }
                catch { /* SWALLOW */ }

                Connection = null;
            }

            Connection = connection;

            AddEventHandlers(connection);

            ExitLock();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void EnterLock() => _hostLock.Wait();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected Task EnterLockAsync() => _hostLock.WaitAsync();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void ExitLock() => _hostLock.Release();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected virtual void AddEventHandlers(IConnection connection)
        {
            connection.ConnectionBlocked += ConnectionBlocked;
            connection.ConnectionUnblocked += ConnectionUnblocked;
            connection.ConnectionShutdown += ConnectionClosed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected virtual void RemoveEventHandlers(IConnection connection)
        {
            connection.ConnectionBlocked -= ConnectionBlocked;
            connection.ConnectionUnblocked -= ConnectionUnblocked;
            connection.ConnectionShutdown -= ConnectionClosed;
        }

        protected virtual void ConnectionClosed(object sender, ShutdownEventArgs e)
        {
            EnterLock();
            _logger.LogWarning(e.ReplyText);
            Closed = true;
            ExitLock();
        }

        protected virtual void ConnectionBlocked(object sender, ConnectionBlockedEventArgs e)
        {
            EnterLock();
            _logger.LogWarning(e.Reason);
            Blocked = true;
            ExitLock();
        }

        protected virtual void ConnectionUnblocked(object sender, EventArgs e)
        {
            EnterLock();
            _logger.LogInformation("Connection unblocked!");
            Blocked = false;
            ExitLock();
        }

        private const int CloseCode = 200;
        private const string CloseMessage = "HouseofCat.RabbitMQ manual close initiated.";

        public void Close() => Connection.Close(CloseCode, CloseMessage);

        /// <summary>
        /// Due to the complexity of the RabbitMQ Dotnet Client there are a few odd scenarios.
        /// Just casually check Health() when looping through Connections, skip when not Healthy.
        /// <para>AutoRecovery = False yields results like Closed, Dead, and IsOpen will be true, true, false or false, false, true.</para>
        /// <para>AutoRecovery = True, yields difficult results like Closed, Dead, And IsOpen will be false, false, false or true, true, true (and other variations).</para>
        /// </summary>
        public async Task<bool> HealthyAsync()
        {
            await EnterLockAsync().ConfigureAwait(false);

            if (Closed && Connection.IsOpen)
            { Closed = false; } // Means a Recovery took place.
            else if (Dead && Connection.IsOpen)
            { Dead = false; } // Means a Miracle took place.

            ExitLock();

            return Connection.IsOpen && !Blocked; // TODO: See if we can incorporate Dead/Closed observations.
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposedValue)
            {
                return;
            }

            if (disposing)
            {
                _hostLock.Dispose();
            }

            Connection = null;
            _disposedValue = true;
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
