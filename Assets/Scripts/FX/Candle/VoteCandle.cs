using System.Collections.Generic;
using AudioSystem;
using Board;
using Characters;
using CorruptionDuPortail.Domain;
using FMODUnity;
using GameLogic;
using GameLogic.GameStates;
using Presentation;
using UnityEngine;
using UnityEngine.Assertions;

namespace FX.Candle
{
    /// <summary>
    /// The vote candle (board task T19, "rendre les révélations d'enchaînement encore plus dramatiques", point 1; maquettes
    /// compte-a-rebours v1: A3 the candle + A4 the room going dark with it, B1 a clock that speeds up, C2 the last minute
    /// louder and louder). A candle on the table, by the player's own cards, is lit when the vote opens and melts while
    /// it runs. In the last minute a clock starts ticking quietly, the flame grows nervous and the room around the table
    /// dims; the final seconds are played in full (the clock races, the flame jumps at each tick, only the candle lights
    /// the table). When the vote closes the candle is blown out: a wisp of smoke, and the chaining starts in the dark.
    /// When everyone has voted, the few seconds left become that final phase (<see cref="VoteCountdown"/>).
    /// Presentation only, local: each player sees their own candle, driven by the replicated vote timer.
    /// </summary>
    public class VoteCandle : MonoBehaviour
    {
        // Lane A (scene-wired), narrowed to the read slice like the lamps.
        [SerializeField] private GameManager _gameManager;
        [Tooltip("The room's light over the table (MainLight): dimmed as the tension rises.")]
        [SerializeField] private Light _roomLight;
        [SerializeField] private CatSpotlights _catSpotlights;
        [Tooltip("The seated view's floor (unlit wood): dimmed with the room.")]
        [SerializeField] private Renderer _seatedFloor;
        [Tooltip("The cards: the candle never stands on a card or its vote panel.")]
        [SerializeField] private BoardManager _boardManager;
        [Tooltip("The table top the candle stands on.")]
        [SerializeField] private Renderer _table;
        [Tooltip("Room the candle needs around its centre at full size (its dish plus a margin), world units.")]
        [SerializeField] private float _footprintRadius = 1.8f;
        [Tooltip("Its size, chosen on the maquettes (compte-a-rebours v3, 0.79). Smaller still where the table is crowded.")]
        [SerializeField] private float _baseScale = 0.79f;
        [Tooltip("Furthest from the table's middle (x) it may be moved to and still be seen from the seat, head at rest.")]
        [SerializeField] private float _maxSideOffset = 9f;
        [Tooltip("Furthest from the player (world z) its dish may reach: it stays on the player's side of the table.")]
        [SerializeField] private float _maxDepth = -0.5f;
        [Tooltip("The local player, for their turn at night.")]
        [SerializeField] private CharacterManager _characterManager;
        [Tooltip("Seconds for a burnt candle to make itself new when a timer starts, before it lights.")]
        [SerializeField] private float _regrowSeconds = 1.2f;

        [Header("Parts")]
        [Tooltip("Shown while the candle is on the table, hidden the rest of the game.")]
        [SerializeField] private GameObject _visual;
        [SerializeField] private MeshFilter _wax;
        [SerializeField] private MeshFilter _holder;
        [SerializeField] private MeshFilter _wick;
        [SerializeField] private MeshFilter _flame;
        [SerializeField] private MeshFilter _glow;
        [SerializeField] private Light _flameLight;
        [SerializeField] private ParticleSystem _smoke;
        [SerializeField] private CandleMesh.Shape _shape = CandleMesh.Shape.Default;

        [Header("Flame and light")]
        [SerializeField] private Vector2 _flameSize = new Vector2(0.46f, 1.25f);
        [SerializeField] private float _glowSize = 3.2f;
        [Tooltip("Light of the candle while the vote is calm.")]
        [SerializeField] private float _lightIntensity = 16f;
        [Tooltip("Light of the candle at the very end, when it is the only light left.")]
        [SerializeField] private float _lightIntensityTense = 22f;
        [SerializeField] private float _igniteSeconds = 0.6f;
        [SerializeField] private float _snuffSeconds = 0.18f;

        [Header("The room going dark")]
        [Tooltip("The room light at full tension, as a share of its normal intensity.")]
        [Range(0f, 1f)] [SerializeField] private float _roomAtFullTension = 0.12f;
        [Range(0f, 1f)] [SerializeField] private float _catLightsAtFullTension = 0.4f;
        [Range(0f, 1f)] [SerializeField] private float _floorAtFullTension = 0.25f;
        [Tooltip("After the candle is blown out the room stays this dark until the chaining starts.")]
        [Range(0f, 1f)] [SerializeField] private float _roomAfterSnuff = 0.2f;
        [Tooltip("Seconds for the room to light up again once the vote is over.")]
        [SerializeField] private float _restoreSeconds = 1.5f;

        [Header("Sounds")]
        [Tooltip("The clock: tic and tac alternate.")]
        [SerializeField] private EventReference _tickSound;
        [SerializeField] private EventReference _tockSound;
        [SerializeField] private EventReference _snuffSound;
        [SerializeField] private EventReference _igniteSound;

        private static readonly int FlamePosId = Shader.PropertyToID("_FlamePosWS");
        private static readonly int FlameStrengthId = Shader.PropertyToID("_FlameStrength");
        private static readonly int AgitationId = Shader.PropertyToID("_Agitation");
        private static readonly int LeanId = Shader.PropertyToID("_Lean");
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");

        private enum Phase { Hidden, Lit, Out }

        private IGameStateQuery Query => _gameManager;

        private Phase _phase = Phase.Hidden;
        private Mesh _waxMesh;
        private float _builtMelt = -1f;
        private int _tickCount;
        private readonly VoteClock _clock = new VoteClock();
        private float _pulse;
        private float _phaseTime;
        private float _tension;
        private float _darkness;
        private float _appliedDarkness = -1f;
        private float _roomBaseIntensity;
        private float _floorBaseBrightness;
        private MaterialPropertyBlock _block;
        private Vector3 _home;
        private TimerKind _litFor;
        private float _regrowFrom;
        private Character _localCharacter;
        private float _nextLocalLookup;
        private ICharacterQuery CharacterQuery => _characterManager;
        private readonly List<TableRect> _cardRects = new List<TableRect>();
        private readonly Vector3[] _corners = new Vector3[4];

        private void Awake()
        {
            Assert.IsNotNull(_gameManager, "VoteCandle._gameManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(_roomLight, "VoteCandle._roomLight is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(_visual, "VoteCandle._visual is not wired");
            _block = new MaterialPropertyBlock();
            _home = transform.position;
            _roomBaseIntensity = _roomLight != null ? _roomLight.intensity : 0f;
            if (_seatedFloor != null && _seatedFloor.sharedMaterial != null && _seatedFloor.sharedMaterial.HasProperty(BrightnessId))
            {
                _floorBaseBrightness = _seatedFloor.sharedMaterial.GetFloat(BrightnessId);
            }
            // The dish's foot goes below its floor: stand it on the table, not in it.
            _visual.transform.localPosition = new Vector3(0f, CandleMesh.DishDepth, 0f);
            BuildStaticMeshes();
            ConfigureSmoke();
            _visual.SetActive(false);
        }

        // A thin wisp rising and curling for a couple of seconds after the candle is blown out.
        private void ConfigureSmoke()
        {
            if (_smoke == null)
            {
                return;
            }
            _smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule _main = _smoke.main;
            _main.playOnAwake = false;
            _main.loop = false;
            _main.duration = 1.4f;
            _main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.2f);
            _main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            _main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
            _main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            _main.startColor = new Color(0.82f, 0.8f, 0.78f, 0.55f);
            _main.gravityModifier = -0.03f;
            _main.simulationSpace = ParticleSystemSimulationSpace.World;
            _main.maxParticles = 80;

            ParticleSystem.EmissionModule _emission = _smoke.emission;
            _emission.rateOverTime = new ParticleSystem.MinMaxCurve(22f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f));
            _emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6) });

            ParticleSystem.ShapeModule _shape2 = _smoke.shape;
            _shape2.shapeType = ParticleSystemShapeType.Cone;
            _shape2.angle = 4f;
            _shape2.radius = 0.02f;
            _shape2.rotation = new Vector3(-90f, 0f, 0f);

            ParticleSystem.NoiseModule _noise = _smoke.noise;
            _noise.enabled = true;
            _noise.strength = 0.35f;
            _noise.frequency = 0.8f;
            _noise.scrollSpeed = 0.4f;

            ParticleSystem.SizeOverLifetimeModule _size = _smoke.sizeOverLifetime;
            _size.enabled = true;
            _size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3.2f));

            ParticleSystem.ColorOverLifetimeModule _colour = _smoke.colorOverLifetime;
            _colour.enabled = true;
            var _gradient = new Gradient();
            _gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.7f, 0.7f, 0.72f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.75f, 0.12f), new GradientAlphaKey(0.3f, 0.6f), new GradientAlphaKey(0f, 1f) });
            _colour.color = _gradient;

            ParticleSystemRenderer _renderer = _smoke.GetComponent<ParticleSystemRenderer>();
            _renderer.renderMode = ParticleSystemRenderMode.Billboard;
            _renderer.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV, ParticleSystemVertexStream.StableRandomX,
            });
        }

        private void OnDestroy()
        {
            ApplyDarkness(0f);
            if (_waxMesh != null)
            {
                Destroy(_waxMesh);
            }
        }

        private void BuildStaticMeshes()
        {
            _waxMesh = new Mesh { name = "VoteCandle Wax" };
            _wax.sharedMesh = _waxMesh;
            var _holderMesh = new Mesh { name = "VoteCandle Dish" };
            CandleMesh.BuildHolder(_holderMesh);
            _holder.sharedMesh = _holderMesh;
            var _wickMesh = new Mesh { name = "VoteCandle Wick" };
            CandleMesh.BuildWick(_wickMesh, 0.36f, 0.25f);
            _wick.sharedMesh = _wickMesh;
            var _flameMesh = new Mesh { name = "VoteCandle Flame" };
            CandleMesh.BuildQuad(_flameMesh, 0f);
            _flame.sharedMesh = _flameMesh;
            var _glowMesh = new Mesh { name = "VoteCandle Glow" };
            CandleMesh.BuildQuad(_glowMesh, -0.5f);
            _glow.sharedMesh = _glowMesh;
            Melt(0f);
        }

        private void Update()
        {
            float _dt = Time.deltaTime;
            GameState _state = _gameManager != null && _gameManager.IsSpawned
                ? Query.GetGameState(Query.currentGameStateIndex.Value)
                : null;
            bool _inGame = _state != null && !(_state is LobbyState);
            Timer _timer = CurrentTimer(_state);

            if (!_inGame)
            {
                if (_phase != Phase.Hidden)
                {
                    _phase = Phase.Hidden;
                    _visual.SetActive(false);
                }
            }
            else
            {
                if (_phase == Phase.Hidden)
                {
                    // Always on the table during a game (maquettes v3): burnt down and out between two timers.
                    _phase = Phase.Out;
                    _phaseTime = 10f;
                    _visual.SetActive(true);
                    Melt(1f);
                    DriveFlame(0f, 0f, 0f);
                    SetStrength(_glow, 0f);
                }
                if (_timer.Kind != TimerKind.None)
                {
                    if (_phase != Phase.Lit || _timer.Kind != _litFor)
                    {
                        Ignite(_timer);
                    }
                    Burn(_timer, _dt);
                }
                else
                {
                    if (_phase == Phase.Lit)
                    {
                        Snuff();
                    }
                    AfterSnuff(_dt, _state is VoteRecapState);
                }
            }

            if (!(_state is VoteState) && !(_state is VoteRecapState))
            {
                _darkness = Mathf.MoveTowards(_darkness, 0f, _dt / Mathf.Max(0.01f, _restoreSeconds));
            }
            ApplyDarkness(_darkness);
        }

        // ---- which timer the candle burns for: the vote, or the local player's own turn at night (maquettes v3, N2)

        private enum TimerKind { None, Vote, Awakening }

        private readonly struct Timer
        {
            public readonly TimerKind Kind;
            public readonly float Left;
            public readonly float Duration;

            public Timer(TimerKind _kind, float _left, float _duration)
            {
                Kind = _kind;
                Left = _left;
                Duration = _duration;
            }
        }

        private Timer CurrentTimer(GameState _state)
        {
            if (_state is VoteState _vote)
            {
                return new Timer(TimerKind.Vote, _vote.voteTimer, _vote.voteDuration);
            }
            if (_state is AwakeningState _night && IsLocalAwake())
            {
                // A client's maximum may arrive after its timer: never shorter than the time left.
                float _max = Mathf.Max(_night.currentAwakeningMaxTime, _night.currentAwakeningTimer, 1f);
                return new Timer(TimerKind.Awakening, _night.currentAwakeningTimer, _max);
            }
            return new Timer(TimerKind.None, 0f, 0f);
        }

        private bool IsLocalAwake()
        {
            if (_localCharacter == null && _characterManager != null && Time.unscaledTime >= _nextLocalLookup)
            {
                _nextLocalLookup = Time.unscaledTime + 0.5f;
                _localCharacter = CharacterQuery.GetLocalCharacter(false);
            }
            return _localCharacter != null && _localCharacter.isAwakened.Value;
        }

        // ---- a timer starts: the burnt-down candle makes itself new again (maquettes v3, B3), then lights

        private void Ignite(Timer _timer)
        {
            _litFor = _timer.Kind;
            _phase = Phase.Lit;
            _phaseTime = 0f;
            // The cards are laid out by now (at the start of the game they were still flying to their places).
            PlaceOnTable();
            _clock.Start(_timer.Duration, _timer.Left);
            _pulse = 0f;
            _tickCount = 0;
            _regrowFrom = Mathf.Max(0f, _builtMelt);
            if (_smoke != null)
            {
                _smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            _glow.transform.localScale = Vector3.one * _glowSize;
        }

        private void Burn(Timer _timer, float _dt)
        {
            _phaseTime += _dt;
            bool _vote = _timer.Kind == TimerKind.Vote;

            // Follow the replicated timer (VoteClock): a client gets the vote's once a second and counts down in between.
            bool _tick = _clock.Advance(_timer.Left, _dt);
            if (_vote)
            {
                if (_tick)
                {
                    PlaySound(_tickCount++ % 2 == 0 ? _tickSound : _tockSound, VoteCountdown.TickVolume(_clock.TimeLeft, _clock.FinalSpan));
                    _pulse = 1f;
                }
                _tension = _clock.Tension;
                _darkness = _tension;
            }
            else
            {
                // At night: the flame alone grows nervous at the end of the turn; no clock, the room untouched.
                _tension = VoteCountdown.TurnAgitation(_clock.TimeLeft, _timer.Duration);
            }
            _pulse *= Mathf.Exp(-_dt / 0.14f);

            // A new candle first: the burnt one makes itself whole again (for now its melt played backward, the effect
            // is still being chosen on the maquettes, v4), then it lights.
            float _regrow = Mathf.Clamp01(_phaseTime / Mathf.Max(0.01f, _regrowSeconds));
            float _rewind = _regrow * _regrow * (3f - 2f * _regrow);
            Melt(Mathf.Lerp(_regrowFrom, _clock.Melt, _rewind));
            if (_regrow < 1f)
            {
                DriveFlame(0f, 0f, 0f);
                return;
            }
            float _lit = _phaseTime - _regrowSeconds;
            if (_lit - _dt <= 0f)
            {
                PlaySound(_igniteSound, 0.8f);
            }
            float _ignite = Mathf.Clamp01(_lit / Mathf.Max(0.01f, _igniteSeconds));
            // A small flare as it catches, then it settles.
            float _strength = Mathf.SmoothStep(0f, 1f, _ignite) * (1f + 0.35f * Mathf.Sin(_ignite * Mathf.PI));
            DriveFlame(_strength, _tension, _pulse);
        }

        // ---- the timer ends: blown out, smoke; after the vote the room stays dark until the chaining

        private void Snuff()
        {
            _phase = Phase.Out;
            _phaseTime = 0f;
            if (_smoke != null)
            {
                _smoke.transform.position = FlameBase();
                _smoke.Play(true);
            }
            // The vote closes on a clack; a turn at night ends more quietly.
            PlaySound(_snuffSound, _litFor == TimerKind.Vote ? 1f : 0.5f);
            _litFor = TimerKind.None;
        }

        private void AfterSnuff(float _dt, bool _afterVote)
        {
            _phaseTime += _dt;
            float _out = 1f - Mathf.Clamp01(_phaseTime / Mathf.Max(0.01f, _snuffSeconds));
            DriveFlame(_out * (1f + 0.4f * _out), 1f, 0f);
            // The ember on the wick fades after the flame.
            float _ember = 1f - Mathf.Clamp01((_phaseTime - _snuffSeconds) / 1.6f);
            if (_out <= 0f)
            {
                _glow.transform.localScale = Vector3.one * (_glowSize * 0.12f);
                SetStrength(_glow, 0.9f * _ember * _ember);
            }
            if (_afterVote)
            {
                // Without the candle's light the room comes back a little, to _roomAfterSnuff, and waits for the chaining.
                float _target = Mathf.Clamp01((1f - _roomAfterSnuff) / Mathf.Max(0.01f, 1f - _roomAtFullTension));
                _darkness = Mathf.MoveTowards(_darkness, Mathf.Min(_darkness, _target), _dt);
            }
        }

        // ---- where it stands: its own place by the player's cards, or the nearest free one (never on a card)

        private void PlaceOnTable()
        {
            if (_boardManager == null || _table == null)
            {
                return;
            }
            _cardRects.Clear();
            foreach (Card _card in _boardManager.visibleCards)
            {
                if (_card == null)
                {
                    continue;
                }
                Bounds _bounds = new Bounds(_card.transform.position, Vector3.zero);
                foreach (Collider _collider in _card.GetComponentsInChildren<Collider>())
                {
                    _bounds.Encapsulate(_collider.bounds);
                }
                if (_card.voteCanvas != null && _card.voteCanvas.transform is RectTransform _panel)
                {
                    _panel.GetWorldCorners(_corners);
                    foreach (Vector3 _corner in _corners)
                    {
                        _bounds.Encapsulate(_corner);
                    }
                }
                // A hovered card grows and its vote panel slides down: keep a margin.
                _bounds.Expand(new Vector3(0.35f, 0f, 0.5f));
                _cardRects.Add(new TableRect(_bounds.min.x, _bounds.min.z, _bounds.max.x, _bounds.max.z));
            }
            Bounds _top = _table.bounds;
            float _radius = _footprintRadius * _baseScale;
            CandleSpotResult _spot = CandleSpot.Find(_home.x, _home.z, _radius,
                new TableRect(_top.min.x, _top.min.z, _top.max.x, _top.max.z), _cardRects, _maxSideOffset, _maxDepth);
            transform.position = new Vector3(_spot.X, _top.max.y, _spot.Z);
            transform.localScale = Vector3.one * (_spot.Scale * _baseScale);
            if (!_spot.Free)
            {
                Debug.LogWarning($"[CANDLE] No free place on the table for the vote candle ({_cardRects.Count} cards): left corner, smallest.");
            }
        }

        // ---- parts

        private void Melt(float _melt)
        {
            if (Mathf.Abs(_melt - _builtMelt) < 0.002f)
            {
                return;
            }
            _builtMelt = _melt;
            CandleMesh.BuildCandle(_waxMesh, _shape, _melt);
            float _pool = CandleMesh.PoolHeight(_shape, _melt);
            _wick.transform.localPosition = new Vector3(0f, _pool, 0f);
        }

        private Vector3 FlameBase()
        {
            float _pool = CandleMesh.PoolHeight(_shape, Mathf.Max(0f, _builtMelt));
            return _visual.transform.TransformPoint(new Vector3(0.03f, _pool + 0.22f, 0f));
        }

        private void DriveFlame(float _strength, float _tension, float _pulse)
        {
            float _t = Time.time;
            float _noise = Mathf.PerlinNoise(_t * 2.3f, 0.37f) - 0.5f;
            float _nervous = 0.15f + 0.85f * _tension;
            float _lean = Mathf.Clamp((Mathf.PerlinNoise(_t * 0.8f, 5.1f) - 0.5f) * 2f * (0.12f + 0.75f * _tension) + 0.08f, -1f, 1f);
            float _height = 1f + 0.07f * _noise * (1f + 2f * _tension) + 0.3f * _pulse * (0.3f + 0.7f * _tension);

            float _pool = CandleMesh.PoolHeight(_shape, Mathf.Max(0f, _builtMelt));
            Vector3 _base = new Vector3(_lean * 0.07f, _pool + 0.22f, 0f);
            _flame.transform.localPosition = _base;
            _flame.transform.localScale = new Vector3(_flameSize.x * (1f - 0.08f * _pulse), _flameSize.y * _height, 1f);
            _flame.gameObject.SetActive(_strength > 0.001f);

            _block.Clear();
            _block.SetFloat(AgitationId, Mathf.Clamp01(0.12f + 0.78f * _tension + 0.4f * _pulse));
            _block.SetFloat(LeanId, _lean);
            _block.SetFloat(StrengthId, Mathf.Clamp01(_strength));
            _flame.GetComponent<Renderer>().SetPropertyBlock(_block);

            float _flicker = 1f + _noise * 0.16f * _nervous + 0.25f * _pulse;
            _glow.transform.localPosition = _base + new Vector3(_lean * 0.15f, 0.55f, 0f);
            _glow.transform.localScale = Vector3.one * _glowSize;
            SetStrength(_glow, _strength * _flicker * 0.6f);

            if (_flameLight != null)
            {
                _flameLight.transform.localPosition = _base + new Vector3(_lean * 0.2f, 0.5f, 0f);
                _flameLight.intensity = Mathf.Lerp(_lightIntensity, _lightIntensityTense, _tension) * _flicker * _strength;
                _flameLight.enabled = _strength > 0.001f;
            }

            Renderer _waxRenderer = _wax.GetComponent<Renderer>();
            _block.Clear();
            _block.SetVector(FlamePosId, _visual.transform.TransformPoint(_base + new Vector3(0f, 0.4f, 0f)));
            _block.SetFloat(FlameStrengthId, _strength * _flicker);
            _waxRenderer.SetPropertyBlock(_block);
        }

        private void SetStrength(MeshFilter _part, float _strength)
        {
            _block.Clear();
            _block.SetFloat(StrengthId, Mathf.Max(0f, _strength));
            _part.GetComponent<Renderer>().SetPropertyBlock(_block);
        }

        // ---- the room

        /// <summary>0 = the room as tuned, 1 = at full tension (only the candle left).</summary>
        private void ApplyDarkness(float _level)
        {
            if (Mathf.Approximately(_level, _appliedDarkness))
            {
                return;
            }
            _appliedDarkness = _level;
            if (_roomLight != null)
            {
                _roomLight.intensity = _roomBaseIntensity * Mathf.Lerp(1f, _roomAtFullTension, _level);
            }
            if (_catSpotlights != null)
            {
                _catSpotlights.Dim = Mathf.Lerp(1f, _catLightsAtFullTension, _level);
            }
            if (_seatedFloor != null && _floorBaseBrightness > 0f)
            {
                _block.Clear();
                if (_level > 0.0001f)
                {
                    _block.SetFloat(BrightnessId, _floorBaseBrightness * Mathf.Lerp(1f, _floorAtFullTension, _level));
                }
                _seatedFloor.SetPropertyBlock(_block);
            }
        }

        private static void PlaySound(EventReference _event, float _volume)
        {
            if (_event.IsNull || GameAudioManager.instance == null)
            {
                return;
            }
            GameAudioManager.instance.PlayOneShot(_event, _volume);
        }
    }
}
