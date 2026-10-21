using System;
using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Who hears whom on the in-game voice (board task T22, GD: "le jour pour tout le monde et la nuit seulement pour
    /// les anomalies et le messager et son correspondant"). Outside the night every seated player hears every other
    /// one. At night a voice reaches only the players who share a private chat channel with the speaker: the anomaly
    /// channel (Œil du Néant) and the Lié par l'encre channel of the messager and his correspondent are the only private
    /// channels, so the voice follows the server's chat membership instead of a second list of its own. Who hears a
    /// voice is deduction information: the server routes, a client never decides. Decision-only, seats only (the
    /// adapter maps seats to connections, skips simulated bots and departed players).
    /// </summary>
    public static class VoiceRouting
    {
        /// <summary>The seats that must receive <paramref name="speaker"/>'s voice, in a stable order, never the speaker.</summary>
        /// <param name="connectedSeats">Seats currently played by a connection (the speaker included or not).</param>
        /// <param name="privateChannelsOf">Server membership: private chat channels a seat belongs to.</param>
        /// <param name="membersOf">Server membership: seats of a private chat channel.</param>
        public static List<ulong> Listeners(ulong speaker, bool isNight, IEnumerable<ulong> connectedSeats,
            Func<ulong, IReadOnlyList<int>> privateChannelsOf, Func<int, IReadOnlyList<ulong>> membersOf)
        {
            var _connected = new HashSet<ulong>(connectedSeats);
            var _listeners = new SortedSet<ulong>();
            if (!isNight)
            {
                foreach (ulong _seat in _connected)
                {
                    if (_seat != speaker)
                    {
                        _listeners.Add(_seat);
                    }
                }
                return new List<ulong>(_listeners);
            }

            foreach (int _channel in privateChannelsOf(speaker))
            {
                foreach (ulong _member in membersOf(_channel))
                {
                    if (_member != speaker && _connected.Contains(_member))
                    {
                        _listeners.Add(_member);
                    }
                }
            }
            return new List<ulong>(_listeners);
        }

        /// <summary>
        /// Client side: whether the microphone may open at all. At night a player outside every private channel has
        /// nobody to talk to, so his voice is not even recorded (the server would drop it anyway).
        /// </summary>
        public static bool MaySpeak(bool isNight, bool hasPrivateChannel) => !isNight || hasPrivateChannel;

        /// <summary>
        /// Voice packets travel unreliable: a late packet (sequence older than the last one played, 16-bit wrap
        /// included) is dropped instead of being played out of order.
        /// </summary>
        public static bool IsNewerSequence(ushort incoming, ushort last) => (short)(incoming - last) > 0;
    }
}
