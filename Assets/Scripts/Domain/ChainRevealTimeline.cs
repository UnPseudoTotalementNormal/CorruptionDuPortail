using System;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// The chained cat's moment (board task T19, points 2 to 4, maquettes enchainement v2: E1 + S2 + F2 + Y1): seen from
    /// the front, the cat is lifted slowly by a force while the artist's « Getting Chained » animation plays, let go, falls
    /// back on its seat and is flattened; the sound is sucked out just before the impact, and the flattest pose stays
    /// frozen half a second too long. Every time is in seconds since the « Getting Chained » clip started (it is followed
    /// by « Getting Chained 2 »: flattened at <see cref="Impact"/>, flattest at <see cref="Flat"/>).
    /// Pure: no engine type, testable in EditMode.
    /// </summary>
    public static class ChainRevealTimeline
    {
        /// <summary>The force starts lifting the cat.</summary>
        public const float LiftStart = 0.4f;

        /// <summary>The force lets go: the cat falls from here.</summary>
        public const float Release = 2.55f;

        /// <summary>The cat hits its seat (the clip's own squash).</summary>
        public const float Impact = 2.96f;

        /// <summary>The flattest pose of the clip, held by the freeze.</summary>
        public const float Flat = 3.21f;

        /// <summary>How long the flattest pose is held.</summary>
        public const float FreezeSeconds = 0.5f;

        /// <summary>The sound starts being sucked out (muffled and quieter).</summary>
        public const float SuckStart = Impact - 0.85f;

        /// <summary>From here to the impact: nothing at all.</summary>
        public const float SilenceStart = Impact - 0.25f;

        /// <summary>The music comes back over this many seconds once the freeze is over.</summary>
        public const float MusicBackSeconds = 0.7f;

        /// <summary>The camera's jump toward the cat at the impact: duration, then held during the freeze.</summary>
        public const float PunchInSeconds = 0.08f;

        /// <summary>How long the camera takes to come back after the freeze.</summary>
        public const float PunchOutSeconds = 0.5f;

        /// <summary>Lift height, as a share of the cat's height, at the top of its rise.</summary>
        public const float LiftShare = 0.6f;

        /// <summary>
        /// How high the force holds the cat, as a share of the full lift [0, ~1.05]: a slow rise, a slight float while
        /// it is chained, then a fall that reaches 0 at the impact (gravity: it accelerates).
        /// </summary>
        public static float Lift(float t)
        {
            if (t <= LiftStart || t >= Impact)
            {
                return 0f;
            }
            if (t < Release)
            {
                float rise = SmoothStep((t - LiftStart) / (Release - LiftStart));
                float bob = t > 0.9f ? 0.05f * (float)Math.Sin((t - 0.9f) * 5.5f) * rise : 0f;
                return rise + bob;
            }
            float fall = (t - Release) / (Impact - Release);
            return 1f - fall * fall;
        }

        /// <summary>True while the flattest pose is held (the animation stopped).</summary>
        public static bool IsFrozen(float t) => t >= Flat && t < Flat + FreezeSeconds;

        /// <summary>
        /// Volume [0, 1] of everything but the splatch: full, sucked out from <see cref="SuckStart"/> to
        /// <see cref="SilenceStart"/>, then silent until the impact; back at once with the splatch.
        /// </summary>
        public static float SuckVolume(float t)
        {
            if (t < SuckStart || t >= Impact)
            {
                return 1f;
            }
            if (t >= SilenceStart)
            {
                return 0f;
            }
            float k = (t - SuckStart) / (SilenceStart - SuckStart);
            return 1f - SmoothStep(k);
        }

        /// <summary>
        /// Low-pass cut-off (Hz) on everything while the sound is sucked out: open, then closing down to a muffle.
        /// </summary>
        public static float SuckCutoff(float t)
        {
            const float Open = 22000f, Closed = 180f;
            if (t < SuckStart || t >= Impact)
            {
                return Open;
            }
            float k = Math.Min(1f, (t - SuckStart) / (SilenceStart - SuckStart));
            // Exponential sweep: an ear hears the cut-off by octaves.
            return (float)(Open * Math.Pow(Closed / Open, k));
        }

        /// <summary>
        /// Volume [0, 1] of the music: out from the suck to the end of the freeze (the splatch rings alone), then back.
        /// </summary>
        public static float MusicVolume(float t)
        {
            if (t < SuckStart)
            {
                return 1f;
            }
            if (t < Impact)
            {
                return SuckVolume(t);
            }
            float back = (t - (Flat + FreezeSeconds)) / MusicBackSeconds;
            return back <= 0f ? 0f : Math.Min(1f, back);
        }

        /// <summary>
        /// How far the camera has jumped toward the cat [0, 1]: a snap at the impact, held through the freeze, then
        /// eased back.
        /// </summary>
        public static float PunchIn(float t)
        {
            if (t < Impact)
            {
                return 0f;
            }
            if (t < Impact + PunchInSeconds)
            {
                return (t - Impact) / PunchInSeconds;
            }
            float hold = Flat + FreezeSeconds;
            if (t < hold)
            {
                return 1f;
            }
            return 1f - SmoothStep((t - hold) / PunchOutSeconds);
        }

        private static float SmoothStep(float x)
        {
            x = x < 0f ? 0f : x > 1f ? 1f : x;
            return x * x * (3f - 2f * x);
        }
    }
}
