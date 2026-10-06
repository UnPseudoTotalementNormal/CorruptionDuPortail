using System.Diagnostics;
using NUnit.Framework;
using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// A client game that dies (crash, kill) leaves the host sending to a closed UDP port. Over loopback / LAN the OS
    /// answers each datagram with ICMP "port unreachable", and on Windows the next receive on the host's socket fails.
    /// Unity Transport must survive that: the host keeps hearing its other clients. (Seen in autoplay: one killed client
    /// made the host deaf within seconds and every other player was dropped.)
    /// Raw Unity Transport drivers, no game code.
    /// </summary>
    public class UdpDeadPeerTests
    {
        [TestCase(true, TestName = "Host_KeepsHearingItsClients_AfterAPeerDies")]
        [TestCase(false, TestName = "Control_PeerStaysAlive")]
        public void Host_KeepsHearingItsClients(bool _peerDies)
        {
            using var _server = NetworkDriver.Create();
            Assert.AreEqual(0, _server.Bind(NetworkEndpoint.LoopbackIpv4.WithPort(0)));
            Assert.AreEqual(0, _server.Listen());
            NetworkEndpoint _serverEndpoint = _server.GetLocalEndpoint();

            using var _alive = NetworkDriver.Create();
            NetworkConnection _aliveToServer = _alive.Connect(_serverEndpoint);
            var _dying = NetworkDriver.Create();
            NetworkConnection _dyingToServer = _dying.Connect(_serverEndpoint);

            var _accepted = new NativeList<NetworkConnection>(Allocator.Temp);
            Pump(() =>
            {
                _server.ScheduleUpdate().Complete();
                _alive.ScheduleUpdate().Complete();
                _dying.ScheduleUpdate().Complete();
                Drain(_alive);
                Drain(_dying);
                NetworkConnection _c;
                while ((_c = _server.Accept()) != default)
                {
                    _accepted.Add(_c);
                }
                return _accepted.Length == 2 &&
                       _alive.GetConnectionState(_aliveToServer) == NetworkConnection.State.Connected &&
                       _dying.GetConnectionState(_dyingToServer) == NetworkConnection.State.Connected;
            }, 5000, "Both clients never connected.");
            Assert.IsTrue(ServerReceives(_server, _alive, _aliveToServer, 1), "Precondition: the host hears the live client.");

            // The dying client's process goes away: its socket closes without a word to the host.
            NetworkConnection _toDead = default;
            foreach (NetworkConnection _c in _accepted)
            {
                if (_server.GetRemoteEndpoint(_c).Port == _dying.GetLocalEndpoint().Port)
                {
                    _toDead = _c;
                }
            }
            Assert.AreNotEqual(default(NetworkConnection), _toDead, "Precondition: the host's connection to the dying client.");
            if (_peerDies)
            {
                _dying.Dispose();
            }

            // The host keeps sending to it (game state, keepalives) until its own timeout, as the game does: 8 s at a
            // game's pace (an update every ~16 ms, a few datagrams each), while the live client keeps talking.
            var _clock = Stopwatch.StartNew();
            int _sent = 0;
            while (_clock.ElapsedMilliseconds < 8000)
            {
                for (int k = 0; k < 4; k++)
                {
                    if (_server.BeginSend(_toDead, out DataStreamWriter _writer) == 0)
                    {
                        _writer.WriteInt(_sent++);
                        _server.EndSend(_writer);
                    }
                }
                if (_alive.BeginSend(_aliveToServer, out DataStreamWriter _beat) == 0)
                {
                    _beat.WriteInt(-1);
                    _alive.EndSend(_beat);
                }
                _server.ScheduleUpdate().Complete();
                _alive.ScheduleUpdate().Complete();
                Drain(_alive);
                if (!_peerDies)
                {
                    _dying.ScheduleUpdate().Complete();
                    Drain(_dying);
                }
                while (_server.PopEvent(out NetworkConnection _, out DataStreamReader _) != NetworkEvent.Type.Empty) { }
                System.Threading.Thread.Sleep(16);
            }
            UnityEngine.Debug.Log($"[UDPDEAD] host sent {_sent} datagrams to the dead peer in 8 s; dead connection state={_server.GetConnectionState(_toDead)}");

            Assert.IsTrue(ServerReceives(_server, _alive, _aliveToServer, 2),
                "After a peer died, the host no longer hears its live client (UDP receive starved by 'port unreachable').");
            _accepted.Dispose();
            if (!_peerDies)
            {
                _dying.Dispose();
            }
        }

        private static bool ServerReceives(NetworkDriver _server, NetworkDriver _client, NetworkConnection _toServer, int _value)
        {
            if (_client.BeginSend(_toServer, out DataStreamWriter _writer) != 0)
            {
                return false;
            }
            _writer.WriteInt(_value);
            _client.EndSend(_writer);

            var _clock = Stopwatch.StartNew();
            while (_clock.ElapsedMilliseconds < 3000)
            {
                _client.ScheduleUpdate().Complete();
                Drain(_client);
                _server.ScheduleUpdate().Complete();
                NetworkEvent.Type _event;
                while ((_event = _server.PopEvent(out NetworkConnection _, out DataStreamReader _reader)) != NetworkEvent.Type.Empty)
                {
                    if (_event == NetworkEvent.Type.Data && _reader.ReadInt() == _value)
                    {
                        return true;
                    }
                }
                System.Threading.Thread.Sleep(5);
            }
            return false;
        }

        private static void Drain(NetworkDriver _driver)
        {
            while (_driver.PopEvent(out NetworkConnection _, out DataStreamReader _) != NetworkEvent.Type.Empty) { }
        }

        private static void Pump(System.Func<bool> _done, int _timeoutMs, string _message)
        {
            var _clock = Stopwatch.StartNew();
            while (!_done())
            {
                if (_clock.ElapsedMilliseconds > _timeoutMs)
                {
                    Assert.Fail(_message);
                }
                System.Threading.Thread.Sleep(5);
            }
        }
    }
}
