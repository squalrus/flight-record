using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace FlightRecord.Audio
{
    /// <summary>A contract message and when it reached the game, on <see cref="AudioClock"/>.</summary>
    public readonly struct ReceivedMessage
    {
        public readonly ContractMessage Message;
        public readonly double Received;

        public ReceivedMessage(ContractMessage message, double received)
        {
            Message = message;
            Received = received;
        }
    }

    /// <summary>
    /// Where the game's audio messages come from. Every source looks the same to the game:
    /// the live bridge (<see cref="UdpMessageSource"/>), a recorded log
    /// (<see cref="ReplayMessageSource"/>), or nothing (<see cref="NullMessageSource"/>), so
    /// development never needs the hardware. Read on the main thread.
    /// </summary>
    public interface IMessageSource : IDisposable
    {
        string Description { get; }
        void Start();

        /// <summary>The next message that has arrived, in arrival order; false if none yet.</summary>
        bool TryRead(out ReceivedMessage message);
    }

    /// <summary>No audio at all: the game must still run (identification and audio are optional).</summary>
    public sealed class NullMessageSource : IMessageSource
    {
        public string Description => "no audio";
        public void Start() { }
        public bool TryRead(out ReceivedMessage message) { message = default; return false; }
        public void Dispose() { }
    }

    /// <summary>
    /// The live bridge: contract messages as JSON datagrams from the Python sidecar
    /// (flightrec/bridge.py), received on a background thread and stamped on arrival.
    /// Listens on the loopback interface only: the bridge is local (D-009).
    /// </summary>
    public sealed class UdpMessageSource : IMessageSource
    {
        public const int DefaultPort = 9750;

        readonly int _port;
        readonly ConcurrentQueue<ReceivedMessage> _queue = new ConcurrentQueue<ReceivedMessage>();
        readonly object _senderLock = new object();
        UdpClient _client;
        Thread _thread;
        volatile bool _running;
        IPEndPoint _sender;
        int _received, _invalid, _skipped, _rejected;

        public UdpMessageSource(int port = DefaultPort) { _port = port; }

        public string Description => $"UDP bridge on 127.0.0.1:{_port}";
        public int Received => Volatile.Read(ref _received);

        /// <summary>Datagrams that were not valid contract messages.</summary>
        public int Invalid => Volatile.Read(ref _invalid);

        /// <summary>Valid messages of a type this game does not know (skipped, per the contract).</summary>
        public int Skipped => Volatile.Read(ref _skipped);

        /// <summary>Messages with a newer schema version (rejected, per the contract).</summary>
        public int Rejected => Volatile.Read(ref _rejected);

        /// <summary>A sender has been heard from, so <see cref="Reply"/> has somewhere to go.</summary>
        public bool HasSender { get { lock (_senderLock) return _sender != null; } }

        public void Start()
        {
            if (_running) return;
            _client = new UdpClient(new IPEndPoint(IPAddress.Loopback, _port));
            _client.Client.ReceiveTimeout = 250;
            _running = true;
            _thread = new Thread(Run) { IsBackground = true, Name = "UdpMessageSource" };
            _thread.Start();
        }

        public bool TryRead(out ReceivedMessage message) => _queue.TryDequeue(out message);

        /// <summary>Send a small JSON datagram back to whoever sent the latest message.</summary>
        public void Reply(string json)
        {
            IPEndPoint to;
            lock (_senderLock) to = _sender;
            if (to == null || _client == null) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            try { _client.Send(bytes, bytes.Length, to); }
            catch (SocketException) { } // the sender went away; it may come back
            catch (ObjectDisposedException) { }
        }

        void Run()
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                byte[] data;
                try { data = _client.Receive(ref from); }
                catch (SocketException) { continue; } // timeout, or a reply that bounced
                catch (ObjectDisposedException) { break; }
                double now = AudioClock.Now;
                lock (_senderLock) _sender = new IPEndPoint(from.Address, from.Port);
                Interlocked.Increment(ref _received);
                try
                {
                    var m = Contract.Parse(Encoding.UTF8.GetString(data));
                    if (m == null) Interlocked.Increment(ref _skipped);
                    else _queue.Enqueue(new ReceivedMessage(m, now));
                }
                catch (UnsupportedVersionException) { Interlocked.Increment(ref _rejected); }
                catch (ContractException) { Interlocked.Increment(ref _invalid); }
                catch (ArgumentException) { Interlocked.Increment(ref _invalid); } // bad UTF-8
            }
        }

        public void Dispose()
        {
            _running = false;
            _client?.Close();
            _thread?.Join(1000);
            _client = null;
        }
    }

    /// <summary>
    /// A recorded message log (JSON lines, e.g. from tools/record_features.py) played back
    /// at its original timing, as if it were arriving live. Unknown types are skipped.
    /// </summary>
    public sealed class ReplayMessageSource : IMessageSource
    {
        readonly string _path;
        readonly double _speed;
        readonly List<ContractMessage> _messages = new List<ContractMessage>();
        int _next;
        double _start, _t0;

        public ReplayMessageSource(string path, double speed = 1.0)
        {
            if (speed <= 0) throw new ArgumentOutOfRangeException(nameof(speed));
            _path = path;
            _speed = speed;
        }

        public string Description => $"replay {Path.GetFileName(_path)}";
        public bool Finished => _next >= _messages.Count;

        public void Start()
        {
            _messages.Clear();
            foreach (var line in File.ReadLines(_path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var m = Contract.Parse(line);
                if (m != null) _messages.Add(m);
            }
            _next = 0;
            _start = AudioClock.Now;
            _t0 = _messages.Count > 0 ? _messages[0].T : 0;
        }

        public bool TryRead(out ReceivedMessage message)
        {
            message = default;
            if (Finished) return false;
            var m = _messages[_next];
            double now = AudioClock.Now;
            if ((m.T - _t0) / _speed > now - _start) return false;
            _next++;
            message = new ReceivedMessage(m, now);
            return true;
        }

        public void Dispose() { }
    }
}
