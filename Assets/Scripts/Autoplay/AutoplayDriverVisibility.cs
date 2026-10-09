#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Avatars;
using Board;
using Board.BoardCameraSystem;
using Board.UI.VoteCanvas;
using GameLogic;
using GameLogic.GameStates;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Autoplay
{
    /// <summary>
    /// Card visibility probe (<c>-autoplay-card-visibility &lt;layouts&gt;</c>, first vote; host or a real client, whose
    /// own seat and avatar give its own first-person view): measures, for
    /// every card of the board, how much of its face and of its vote panel the player really sees in each view (top
    /// board camera, board POV, seated first person at rest and head turned to the card). Pixel truth, not geometry:
    /// the card's face and vote panel are painted flat magenta for one frame; a sample point is visible when magenta
    /// reaches its pixel (anything drawn over the card — a 3D model, another card, a HUD — keeps it off, the screen edge
    /// leaves it off screen). Time is frozen while measuring (vote timer, tweens); the board cameras
    /// still blend (brain on unscaled time). Several card layouts can be tried on the same board in one run
    /// (<see cref="ParseLayouts"/>); the game's own layout is restored afterwards and the vote goes on.
    /// Journal: <c>vis.card</c> per card, <c>vis.view</c> per view summary; annotated captures <c>vis-*.jpg</c>.
    /// </summary>
    public sealed partial class AutoplayDriver
    {
        private bool visibilityDone;

        private sealed class VisLayout
        {
            public string name;
            public bool current;
            public int columns = 6;
            public float scale = 1f;
            public float columnSpacing = float.NaN;
            public float lineSpacing = float.NaN;
            public float firstLineZ = float.NaN;
            public float firstColumnX = float.NaN;
            public bool centre;
            public string preset; // a game preset (CardGridPreset name): placed by the game's own CardLayout
            public string spec;
        }

        private sealed class VisPoint
        {
            public Vector2 screen;
            public string part; // face | button (vote button) | text (vote count text)
            public string cover; // hidden points: best guess of what covers it
            public bool? clickable; // vote button points: does a click there reach this card's vote button
            public bool? reachable; // face points: does pointing there hover this card (not a canvas behind it)
            public string state; // visible | hidden | offscreen
        }

        private BoardManager layoutAppliedTo;

        // Lever -autoplay-card-layout <RaisedLeft|Centred|CentredTop>: play this board arrangement (compare the presets).
        private void UpdateCardLayoutLever()
        {
            BoardManager _board = BoardManager.instance;
            if (string.IsNullOrEmpty(options.cardLayout) || _board == null || _board == layoutAppliedTo)
            {
                return;
            }
            layoutAppliedTo = _board;
            if (!Enum.TryParse(options.cardLayout, true, out CorruptionDuPortail.Domain.CardGridPreset _preset))
            {
                Journal.Record("vis.error", $"unknown card layout {options.cardLayout}");
                return;
            }
            _board.cardGridPreset = _preset;
            if (_board.visibleCards.Count > 0)
            {
                _board.PlaceAllCardsToPosition();
            }
            Journal.Record("card.layout", _preset.ToString());
        }

        private bool seatedViewApplied;

        // Lever -autoplay-seated-view "h=…,pitch=…,r=…,scale=…,round=0|1,stand=0|1" (T16 view comparisons): applied
        // once the seated rig exists, never saved (PlayerPrefs are shared by every process). Journal seated.view.
        private void UpdateSeatedViewLever()
        {
            if (string.IsNullOrEmpty(options.seatedView) || seatedViewApplied)
            {
                return;
            }
            AvatarEmbodiedCamera _camera = FindFirstObjectByType<AvatarEmbodiedCamera>();
            AvatarManager _avatars = FindFirstObjectByType<AvatarManager>();
            TableShapeSwitch _table = FindFirstObjectByType<TableShapeSwitch>(FindObjectsInactive.Include);
            if (_camera == null || _avatars == null || _table == null)
            {
                return;
            }
            seatedViewApplied = true;
            Presentation.SeatedViewOptions _viewOptions = _table.Options;
            foreach (string _pair in options.seatedView.Split(','))
            {
                string[] _kv = _pair.Split('=');
                if (_kv.Length != 2 || !float.TryParse(_kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float _value))
                {
                    Journal.Record("vis.error", $"seated-view: bad pair '{_pair}'");
                    continue;
                }
                switch (_kv[0].Trim())
                {
                    case "h": _camera.HeightOffset = _value; break;
                    case "pitch": _camera.RestPitch = _value; break;
                    case "clamp": _camera.PitchClamp = _value; break;
                    case "r": _avatars.RemoteRingRadius = _value; break;
                    case "scale": _avatars.SeatedAvatarScale = _value; break;
                    case "round": _viewOptions?.SetRoundTable(_value > 0.5f, false); break;
                    case "stand": _viewOptions?.SetCardsStand(_value > 0.5f, false); break;
                    case "standamt": if (_viewOptions != null) { _viewOptions.StandAmount = _value; } break;
                    default: Journal.Record("vis.error", $"seated-view: unknown key '{_kv[0]}'"); break;
                }
            }
            Journal.Record("seated.view", string.Format(CultureInfo.InvariantCulture,
                "h={0} pitch={1} clamp={6} r={2} scale={3} round={4} stand={5} standamt={7}", _camera.HeightOffset, _camera.RestPitch,
                _avatars.RemoteRingRadius, _avatars.SeatedAvatarScale, _viewOptions != null && _viewOptions.RoundTable,
                _viewOptions != null && _viewOptions.CardsStand, _camera.PitchClamp, _viewOptions != null ? _viewOptions.StandAmount : 0f));
        }

        private void UpdateCardVisibility(GameState _state)
        {
            UpdateCardLayoutLever();
            UpdateSeatedViewLever();
            if (string.IsNullOrEmpty(options.cardVisibility) || visibilityDone || !(_state is VoteState))
            {
                return;
            }
            visibilityDone = true;
            inputBusy = true; // nobody votes while the board is measured
            StartCoroutine(CardVisibilityProbe());
        }

        /// <summary>
        /// "name:key=value,…/…" (";" also separates). Keys (board-local units, the card is 6.3 × 8.8 at scale 1): c = cards per line,
        /// s = card scale, sx = column spacing (7·s), sz = line spacing (10.3·s), z0 = first line z (0 = the board's
        /// spawn point; + = towards the far side / top of the top view), x0 = first column x (left-aligned on the
        /// board's left edge), centre=1 = each line centred on the board; preset=&lt;CardGridPreset&gt; = the game's own
        /// placement for that preset. "cur" = the layout the game placed (its current preset).
        /// </summary>
        private static List<VisLayout> ParseLayouts(string _spec)
        {
            var _layouts = new List<VisLayout>();
            foreach (string _entry in _spec.Split(new[] { ';', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] _nameAndKeys = _entry.Split(':', 2);
                var _layout = new VisLayout { name = _nameAndKeys[0].Trim(), spec = _entry.Trim() };
                _layout.current = _layout.name == "cur";
                if (_nameAndKeys.Length > 1)
                {
                    foreach (string _pair in _nameAndKeys[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] _kv = _pair.Split('=', 2);
                        if (_kv.Length == 2 && _kv[0].Trim() == "preset")
                        {
                            _layout.preset = _kv[1].Trim();
                            continue;
                        }
                        if (_kv.Length < 2 || !float.TryParse(_kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float _v))
                        {
                            continue;
                        }
                        switch (_kv[0].Trim())
                        {
                            case "c": _layout.columns = Mathf.Max(1, (int)_v); break;
                            case "s": _layout.scale = _v; break;
                            case "sx": _layout.columnSpacing = _v; break;
                            case "sz": _layout.lineSpacing = _v; break;
                            case "z0": _layout.firstLineZ = _v; break;
                            case "x0": _layout.firstColumnX = _v; break;
                            case "centre": _layout.centre = _v > 0f; break;
                        }
                    }
                }
                _layouts.Add(_layout);
            }
            return _layouts;
        }

        private static TransformComposition.TransformLayer CardLayer(Card _card) => _card.visualComponents.compositor.GetLayer("Transform");

        private static void ApplyLayout(BoardManager _board, List<Card> _cards, VisLayout _layout)
        {
            if (!string.IsNullOrEmpty(_layout.preset) &&
                Enum.TryParse(_layout.preset, true, out CorruptionDuPortail.Domain.CardGridPreset _preset))
            {
                // Exactly what BoardManager.PlaceAllCardsToPosition places for that preset (without its tween).
                var _cardLayout = new CorruptionDuPortail.Domain.CardLayout();
                CorruptionDuPortail.Domain.CardGrid _grid = _cardLayout.GridFor(_cards.Count, _preset);
                Vector3 _anchor = _board.spawnCardPosition.localPosition;
                float _centreX = (_anchor.x + _board.maxCardPosition.localPosition.x) / 2f;
                for (int _i = 0; _i < _cards.Count; _i++)
                {
                    var _placement = _cardLayout.Place(_i, _cards.Count, _grid, _anchor.x, _anchor.y, _anchor.z, _centreX);
                    var _presetLayer = CardLayer(_cards[_i]);
                    _presetLayer.localPosition = new Vector3(_placement.X, _placement.Y, _placement.Z);
                    _presetLayer.localScale = _board.cardPrefab.transform.localScale * _grid.Scale;
                }
                return;
            }
            const float _halfWidth = 3.15f;
            float _s = _layout.scale;
            float _sx = float.IsNaN(_layout.columnSpacing) ? CorruptionDuPortail.Domain.CardLayout.ColumnSpacing * _s : _layout.columnSpacing;
            float _sz = float.IsNaN(_layout.lineSpacing) ? 10.3f * _s : _layout.lineSpacing;
            Vector3 _origin = _board.spawnCardPosition.localPosition;
            float _z0 = _origin.z + (float.IsNaN(_layout.firstLineZ) ? 0f : _layout.firstLineZ);
            float _left = _origin.x - _halfWidth; // the board's left edge (full-size first column's left edge)
            float _x0 = float.IsNaN(_layout.firstColumnX) ? _left + _halfWidth * _s : _layout.firstColumnX;
            int _c = _layout.columns;
            for (int _i = 0; _i < _cards.Count; _i++)
            {
                int _line = _i / _c;
                int _column = _i % _c;
                float _x = _x0 + _column * _sx;
                if (_layout.centre)
                {
                    int _inLine = Mathf.Min(_c, _cards.Count - _line * _c);
                    _x = (_column - (_inLine - 1) / 2f) * _sx;
                }
                var _layer = CardLayer(_cards[_i]);
                _layer.localPosition = new Vector3(_x, _origin.y, _z0 - _line * _sz);
                _layer.localScale = _board.cardPrefab.transform.localScale * _s;
            }
        }

        private IEnumerator CardVisibilityProbe()
        {
            Possess(networkManager.LocalClientId);
            yield return new WaitForSecondsRealtime(options.cardVisibilitySettle); // cards shown, panels slid out

            BoardManager _board = BoardManager.instance;
            BoardCameraManager _views = BoardCameraManager.instance;
            Camera _camera = Camera.main;
            CinemachineBrain _brain = _camera != null ? _camera.GetComponent<CinemachineBrain>() : null;
            if (_board == null || _views == null || _camera == null)
            {
                Journal.Record("vis.error", $"board={_board != null} views={_views != null} camera={_camera != null}");
                inputBusy = false;
                yield break;
            }

            List<Card> _cards = _board.visibleCards.Where(_c => _c).ToList();
            var _saved = _cards.Select(_c => (CardLayer(_c).localPosition, CardLayer(_c).localScale)).ToList();
            float _timeScale = Time.timeScale;
            bool _ignoreTimeScale = _brain != null && _brain.IgnoreTimeScale;
            if (_brain != null)
            {
                _brain.IgnoreTimeScale = true;
            }
            Time.timeScale = 0f;
            // No pointer but the probe's: the user's own mouse resting over the (unfocused) game window hovered cards
            // and tilted them mid-measure. Every card unhovered first.
            BaseInputModule _module = EventSystem.current != null ? EventSystem.current.currentInputModule : null;
            bool _moduleWasEnabled = _module != null && _module.enabled;
            if (_module != null)
            {
                _module.enabled = false;
            }
            // Nor the seated reticle's own hover (it hovers whatever the turned head aims at): with time frozen its
            // early exits wait for the 0.15 s minimum zoom (Card.Update) and the cards stayed raised into the next view.
            var _reticles = FindObjectsByType<Reticle.ReticleInteractor>(FindObjectsSortMode.None).Where(_r => _r.enabled).ToList();
            // Disabling releases its hovers and deactivates it (OnDisable); its arbiter-given state is put back after.
            var _reticleWasActive = _reticles.ToDictionary(_r => _r, _r => GetField<bool>(_r, "_active"));
            _reticles.ForEach(_r => _r.enabled = false);
            foreach (Card _card in _cards)
            {
                _card.OnPointerExit(new PointerEventData(EventSystem.current));
            }
            Time.timeScale = 1f;
            yield return new WaitForSecondsRealtime(0.8f); // unhover animations
            Time.timeScale = 0f;
            // The hover animations need time to run: keep the vote open however long the probe takes.
            VoteState _vote = CurrentState as VoteState;
            float _voteTimer = _vote != null ? _vote.voteTimer : 0f;
            Journal.Record("vis.start", string.Format(CultureInfo.InvariantCulture,
                "cards={0} screen={1}x{2} layouts=\"{3}\" views={4} seat={5} eye={6:0.00},{7:0.00},{8:0.00}",
                _cards.Count, Screen.width, Screen.height, options.cardVisibility, options.cardVisibilityViews, LocalSeat,
                _camera.transform.position.x, _camera.transform.position.y, _camera.transform.position.z));

            string[] _viewNames = options.cardVisibilityViews.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (VisLayout _layout in ParseLayouts(options.cardVisibility))
            {
                if (!_layout.current)
                {
                    ApplyLayout(_board, _cards, _layout);
                }
                else
                {
                    for (int _i = 0; _i < _cards.Count; _i++)
                    {
                        CardLayer(_cards[_i]).localPosition = _saved[_i].localPosition;
                        CardLayer(_cards[_i]).localScale = _saved[_i].localScale;
                    }
                }
                yield return null;
                yield return null;
                foreach (string _viewName in _viewNames)
                {
                    if (_vote != null)
                    {
                        _vote.voteTimer = Mathf.Max(_vote.voteTimer, 120f);
                    }
                    yield return MeasureView(_layout, _viewName.Trim(), _cards, _views);
                }
            }

            for (int _i = 0; _i < _cards.Count; _i++)
            {
                if (_cards[_i])
                {
                    CardLayer(_cards[_i]).localPosition = _saved[_i].localPosition;
                    CardLayer(_cards[_i]).localScale = _saved[_i].localScale;
                }
            }
            SetEmbodiedLook(0f, 0f);
            _views.SetCurrentBoardCamera(BoardCameraIdEnum.SeatedFirstPerson);
            if (_vote != null && CurrentState == _vote)
            {
                _vote.voteTimer = _voteTimer;
            }
            if (_module != null)
            {
                _module.enabled = _moduleWasEnabled;
            }
            _reticles.ForEach(_r =>
            {
                if (_r)
                {
                    _r.enabled = true;
                    _r.SetActive(_reticleWasActive[_r]);
                }
            });
            Time.timeScale = _timeScale;
            if (_brain != null)
            {
                _brain.IgnoreTimeScale = _ignoreTimeScale;
            }
            Journal.Record("vis.done", $"cards={_cards.Count}");
            inputBusy = false;
        }

        // View names: top | powers | fps-rest (seated, head at rest) | fps (head turned to each card); "-hover" = the
        // card is hovered first (vote button out, first-person look-at), like a player about to vote.
        private IEnumerator MeasureView(VisLayout _layout, string _viewName, List<Card> _cards, BoardCameraManager _views)
        {
            if (_viewName == "fps-reticle")
            {
                yield return MeasureReticle(_layout, _cards, _views);
                yield break;
            }
            bool _fps = _viewName.StartsWith("fps", StringComparison.Ordinal);
            bool _hover = _viewName.EndsWith("-hover", StringComparison.Ordinal);
            bool _aim = _fps && !_viewName.StartsWith("fps-rest", StringComparison.Ordinal);
            BoardCameraIdEnum _id = _viewName.StartsWith("top", StringComparison.Ordinal) ? BoardCameraIdEnum.TopBoard
                : _viewName.StartsWith("powers", StringComparison.Ordinal) ? BoardCameraIdEnum.LookAtPowers
                : BoardCameraIdEnum.SeatedFirstPerson;
            _views.SetCurrentBoardCamera(_id);
            AvatarEmbodiedCamera _restCamera = FindFirstObjectByType<AvatarEmbodiedCamera>();
            SetEmbodiedLook(0f, _restCamera != null ? _restCamera.RestPitch : 0f); // the head at rest, as on entry
            yield return new WaitForSecondsRealtime(options.cardVisibilityBlend);
            if (_views.CurrentBoardCameraId != _id)
            {
                Journal.Record("vis.error", $"layout={_layout.name} view={_viewName} camera={_views.CurrentBoardCameraId}");
                yield break;
            }
            if (!_hover)
            {
                yield return SaveClean($"vis-{_layout.name}-{_viewName}-clean");
            }

            var _overview = new List<VisPoint>();
            Texture2D _overviewShot = null;
            var _min = new Dictionary<string, float>();
            var _minFx = new Dictionary<string, float>();
            float _sumFace = 0f;
            int _worst = -1;
            var _hiders = new Dictionary<string, int>();
            for (int _i = 0; _i < _cards.Count; _i++)
            {
                Card _card = _cards[_i];
                string _look = string.Empty;
                if (_aim)
                {
                    _look = AimHeadAt(VisualCentre(_card));
                    yield return WaitStillCamera();
                }
                if (_hover)
                {
                    yield return SetHovered(_card, true);
                    if (_aim)
                    {
                        _look = AimHeadAt(VisualCentre(_card));
                        yield return WaitStillCamera();
                    }
                }

                var _points = new List<VisPoint>();
                Texture2D _shot = null;
                var _cardHiders = new Dictionary<string, int>();
                yield return MeasureCard(_card, _points, _cardHiders, _t => _shot = _t, _aim);
                if (_hover)
                {
                    yield return SetHovered(_card, false);
                }
                if (_shot == null)
                {
                    continue;
                }

                var _ratios = new List<string>();
                bool _bad = false;
                foreach (string _part in Parts)
                {
                    float? _ratio = Ratio(_points, _part, out int _off);
                    if (_ratio == null)
                    {
                        continue;
                    }
                    float _value = _ratio.Value;
                    _ratios.Add(string.Format(CultureInfo.InvariantCulture, "{0}={1:0.0}{2}", _part, _value * 100f, _off > 0 ? $"(off{_off})" : ""));
                    // Same ratio if other cards' markers (beacons) were see-through: what the layout alone hides.
                    float _seeThrough = Ratio(_points, _part, out _, true) ?? _value;
                    if (_seeThrough > _value)
                    {
                        _ratios.Add(string.Format(CultureInfo.InvariantCulture, "{0}-fx={1:0.0}", _part, _seeThrough * 100f));
                    }
                    _minFx[_part] = _minFx.TryGetValue(_part, out float _mf) ? Mathf.Min(_mf, _seeThrough) : _seeThrough;
                    _bad |= _value < 0.995f;
                    if (_part == "face")
                    {
                        _sumFace += _value;
                        if (!_min.ContainsKey("face") || _value < _min["face"])
                        {
                            _worst = _i;
                        }
                    }
                    _min[_part] = _min.TryGetValue(_part, out float _m) ? Mathf.Min(_m, _value) : _value;
                }
                var _facePoints = _points.Where(_p => _p.reachable.HasValue).ToList();
                if (_facePoints.Count > 0)
                {
                    float _reach = _facePoints.Count(_p => _p.reachable == true) / (float)_facePoints.Count;
                    _ratios.Add(string.Format(CultureInfo.InvariantCulture, "reach={0:0.0}", _reach * 100f));
                    _min["reach"] = _min.TryGetValue("reach", out float _mr) ? Mathf.Min(_mr, _reach) : _reach;
                }
                var _buttonPoints = _points.Where(_p => _p.clickable.HasValue).ToList();
                if (_buttonPoints.Count > 0)
                {
                    float _click = _buttonPoints.Count(_p => _p.clickable == true) / (float)_buttonPoints.Count;
                    _ratios.Add(string.Format(CultureInfo.InvariantCulture, "click={0:0.0}", _click * 100f));
                    _min["click"] = _min.TryGetValue("click", out float _mc) ? Mathf.Min(_mc, _click) : _click;
                }
                foreach (var _kv in _cardHiders)
                {
                    _hiders[_kv.Key] = (_hiders.TryGetValue(_kv.Key, out int _n) ? _n : 0) + _kv.Value;
                }
                Journal.Record("vis.card", string.Format(CultureInfo.InvariantCulture,
                    "layout={0} view={1} card={2} owner={3} at={4:0.0},{5:0.0} {6} hidden-by=[{7}]{8}",
                    _layout.name, _viewName, _i, _card.characterInfo != null ? _card.characterInfo.ownerClientId.Value.ToString() : "?",
                    CardLayer(_card).localPosition.x, CardLayer(_card).localPosition.z, string.Join(" ", _ratios),
                    string.Join(",", _cardHiders.OrderByDescending(_kv => _kv.Value).Take(4).Select(_kv => $"{_kv.Key}:{_kv.Value}")), _look));

                if (_aim)
                {
                    if (_bad || _i == 0)
                    {
                        SaveAnnotated(_shot, _points, $"vis-{_layout.name}-{_viewName}-c{_i:00}");
                    }
                    Destroy(_shot);
                    continue;
                }
                // Fixed camera: one annotated overview of every card (on the last frame).
                _overview.AddRange(_points);
                if (_overviewShot != null)
                {
                    Destroy(_overviewShot);
                }
                _overviewShot = _shot;
            }

            if (_overviewShot != null)
            {
                SaveAnnotated(_overviewShot, _overview, $"vis-{_layout.name}-{_viewName}");
                Destroy(_overviewShot);
            }
            Journal.Record("vis.view", string.Format(CultureInfo.InvariantCulture,
                "layout={0} view={1} cards={2} {3} meanFace={4:0.0} worst={5} hidden-by=[{6}] spec=\"{7}\"",
                _layout.name, _viewName, _cards.Count,
                string.Join(" ", Parts.Select(_p => $"min-{_p}=" + (_min.TryGetValue(_p, out float _v) ? (_v * 100f).ToString("0.0", CultureInfo.InvariantCulture) : "-"))
                    .Concat(Parts.Select(_p => $"min-{_p}-fx=" + (_minFx.TryGetValue(_p, out float _v) ? (_v * 100f).ToString("0.0", CultureInfo.InvariantCulture) : "-")))
                    .Append("min-click=" + (_min.TryGetValue("click", out float _vc) ? (_vc * 100f).ToString("0.0", CultureInfo.InvariantCulture) : "-"))
                    .Append("min-reach=" + (_min.TryGetValue("reach", out float _vr) ? (_vr * 100f).ToString("0.0", CultureInfo.InvariantCulture) : "-"))),
                _cards.Count > 0 ? _sumFace / _cards.Count * 100f : 0f, _worst,
                string.Join(",", _hiders.OrderByDescending(_kv => _kv.Value).Take(6).Select(_kv => $"{_kv.Key}:{_kv.Value}")), _layout.spec));
        }

        private static readonly string[] Parts = { "face", "button", "text" };

        // Seated first person, played like a player with the real reticle and time running: aim at the card (it rises
        // and turns to the player), then at its vote button, and hold. Stable = for the whole hold the reticle's
        // control is that vote button and the card stays hovered. A visible button can still be unreachable: the UI
        // raycast hands it to a canvas behind (the characters bar), or the risen card leaves the reticle on what is
        // behind it and the hover flickers.
        private IEnumerator MeasureReticle(VisLayout _layout, List<Card> _cards, BoardCameraManager _views)
        {
            _views.SetCurrentBoardCamera(BoardCameraIdEnum.SeatedFirstPerson);
            SetEmbodiedLook(0f, 0f);
            Time.timeScale = 1f;
            var _reticles = FindObjectsByType<Reticle.ReticleInteractor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).ToList();
            _reticles.ForEach(_r =>
            {
                _r.enabled = true;
                _r.SetActive(true); // the seated Vote's own state (the arbiter's), switched off by the probe meanwhile
            });
            yield return new WaitForSecondsRealtime(options.cardVisibilityBlend);
            float _min = 1f;
            int _worst = -1;
            var _takers = new Dictionary<string, int>();
            for (int _i = 0; _i < _cards.Count; _i++)
            {
                Card _card = _cards[_i];
                VoteCanvas _vote = _card.GetComponentInChildren<VoteCanvas>(true);
                if (_vote == null || _vote.voteButton == null || !_vote.isActiveAndEnabled)
                {
                    continue;
                }
                AimHeadAt(CardCentre(_card));
                yield return WaitStillCamera();
                yield return new WaitForSecondsRealtime(0.8f);
                bool _hoveredFirst = _card.isPointerOver;
                // What the reticle's control track holds while the player looks at the card itself: nothing, or
                // something of this card; never a control behind it (the characters bar outranks cards in the UI raycast).
                string _centreUi = "none";
                foreach (Reticle.ReticleInteractor _reticle in _reticles)
                {
                    GameObject _held = GetField<GameObject>(_reticle, "_uiHandler");
                    if (_held != null)
                    {
                        _centreUi = _held.transform.IsChildOf(_card.transform) ? "own" : _held.name;
                    }
                }
                bool _centreClean = _hoveredFirst && (_centreUi == "none" || _centreUi == "own");
                var _corners = new Vector3[4];
                ((RectTransform)_vote.voteButton.transform).GetWorldCorners(_corners);
                string _look = AimHeadAt((_corners[0] + _corners[2]) / 2f);
                yield return WaitStillCamera();
                yield return new WaitForSecondsRealtime(0.6f);
                int _frames = 0, _good = 0;
                string _ui = "none", _body = "none";
                float _until = Time.realtimeSinceStartup + 0.6f;
                while (Time.realtimeSinceStartup < _until)
                {
                    GameObject _uiTarget = null, _bodyTarget = null;
                    foreach (Reticle.ReticleInteractor _reticle in _reticles)
                    {
                        _uiTarget = _uiTarget ?? GetField<GameObject>(_reticle, "_uiHandler");
                        _bodyTarget = _bodyTarget ?? GetField<GameObject>(_reticle, "_bodyHandler");
                    }
                    bool _onButton = _uiTarget != null && (_uiTarget.transform.IsChildOf(_vote.voteButton.transform) ||
                                                            _vote.voteButton.transform.IsChildOf(_uiTarget.transform));
                    _frames++;
                    if (_onButton && _card.isPointerOver)
                    {
                        _good++;
                    }
                    else
                    {
                        _ui = _uiTarget != null ? _uiTarget.name : "none";
                        _body = _bodyTarget != null ? _bodyTarget.name : "none";
                    }
                    yield return null;
                }
                float _stable = _frames > 0 ? _good / (float)_frames : 0f;
                if (!_centreClean)
                {
                    _stable = 0f; // looking at the card hovers something else (or nothing): not a clean interaction
                    _ui = "centre:" + _centreUi;
                }
                if (_stable < _min)
                {
                    _min = _stable;
                    _worst = _i;
                }
                if (_stable < 1f)
                {
                    _takers[_ui] = (_takers.TryGetValue(_ui, out int _n) ? _n : 0) + 1;
                }
                Journal.Record("vis.card", string.Format(CultureInfo.InvariantCulture,
                    "layout={0} view=fps-reticle card={1} owner={2} at={3:0.0},{4:0.0} reticle={5:0.0} hovered-first={6} centre-ui={10} ui={7} body={8}{9}",
                    _layout.name, _i, _card.characterInfo != null ? _card.characterInfo.ownerClientId.Value.ToString() : "?",
                    CardLayer(_card).localPosition.x, CardLayer(_card).localPosition.z, _stable * 100f, _hoveredFirst, _ui, _body, _look, _centreUi));
                SetEmbodiedLook(0f, 0f);
                yield return new WaitForSecondsRealtime(0.5f);
            }
            _reticles.ForEach(_r => { if (_r) _r.enabled = false; });
            Time.timeScale = 1f;
            yield return new WaitForSecondsRealtime(0.5f); // last unhover
            Time.timeScale = 0f;
            Journal.Record("vis.view", string.Format(CultureInfo.InvariantCulture,
                "layout={0} view=fps-reticle cards={1} min-reticle={2:0.0} worst={3} hidden-by=[{4}] spec=\"{5}\"",
                _layout.name, _cards.Count, _min * 100f, _worst,
                string.Join(",", _takers.OrderByDescending(_kv => _kv.Value).Select(_kv => $"reticle-on:{_kv.Key}:{_kv.Value}")), _layout.spec));
        }

        // Share of the part's sample points that are visible (null: the part is not drawn on this card right now).
        private static bool IsCardEffect(Transform _transform, Card _owner)
        {
            Transform _effects = _owner.transform.Find("Displacer/Pivot/Scaler/CardEffects");
            return _effects != null && _transform.IsChildOf(_effects);
        }

        private static float? Ratio(List<VisPoint> _points, string _part, out int _offscreen, bool _effectsSeeThrough = false)
        {
            int _total = 0, _visible = 0;
            _offscreen = 0;
            foreach (VisPoint _p in _points)
            {
                if (_p.part != _part || _p.state == "reticle")
                {
                    continue;
                }
                _total++;
                if (_p.state == "visible" || (_effectsSeeThrough && _p.state == "hidden" && _p.cover != null &&
                                              _p.cover.StartsWith("effect:", StringComparison.Ordinal)))
                {
                    _visible++;
                }
                else if (_p.state == "offscreen")
                {
                    _offscreen++;
                }
            }
            return _total > 0 ? (float)_visible / _total : null;
        }

        private static Vector3 CardCentre(Card _card)
        {
            Transform _model = _card.transform.Find("Displacer/Pivot/Scaler/3DModel");
            return _model != null ? _model.TransformPoint(new Vector3(0f, 0.5f, -0.1f)) : _card.transform.position;
        }

        // Face: a grid on the card's top face (the 3D model is a unit cube scaled to the card), inset from the edges.
        // Panel: a grid on every vote-panel graphic currently drawn (the vote button, the vote count text).
        private static List<(Vector3 world, string part)> SamplePoints(Card _card)
        {
            var _samples = new List<(Vector3, string)>();
            Transform _model = _card.transform.Find("Displacer/Pivot/Scaler/3DModel");
            if (_model != null)
            {
                const int _nx = 9, _nz = 13;
                for (int _ix = 0; _ix < _nx; _ix++)
                {
                    for (int _iz = 0; _iz < _nz; _iz++)
                    {
                        float _u = Mathf.Lerp(-0.45f, 0.45f, _ix / (float)(_nx - 1));
                        float _v = Mathf.Lerp(-0.46f, 0.46f, _iz / (float)(_nz - 1));
                        _samples.Add((_model.TransformPoint(new Vector3(_u, 0.5f, _v)), "face"));
                    }
                }
            }

            VoteCanvas _vote = _card.GetComponentInChildren<VoteCanvas>(true);
            if (_vote != null && _vote.isActiveAndEnabled)
            {
                // What the player reads: the vote count text and the vote button (its plate and label), sampled on the
                // letters actually drawn (TMP text bounds), not on their whole rects' transparent margins.
                foreach (Graphic _graphic in PanelGraphics(_vote))
                {
                    bool _button = _vote.voteButton != null && _graphic.transform.IsChildOf(_vote.voteButton.transform);
                    if (!_button && _graphic != _vote.votesText)
                    {
                        continue; // backgrounds
                    }
                    Rect _area = _graphic.rectTransform.rect;
                    if (_graphic is TMPro.TMP_Text _text)
                    {
                        Bounds _bounds = _text.textBounds;
                        if (_bounds.size.x <= 0f || _bounds.size.y <= 0f)
                        {
                            continue;
                        }
                        _area = new Rect(_bounds.min.x, _bounds.min.y, _bounds.size.x, _bounds.size.y);
                    }
                    for (int _ix = 0; _ix < 5; _ix++)
                    {
                        for (int _iy = 0; _iy < 3; _iy++)
                        {
                            float _u = Mathf.Lerp(0.1f, 0.9f, _ix / 4f);
                            float _w = Mathf.Lerp(0.2f, 0.8f, _iy / 2f);
                            Vector2 _local = new(Mathf.Lerp(_area.xMin, _area.xMax, _u), Mathf.Lerp(_area.yMin, _area.yMax, _w));
                            _samples.Add((_graphic.rectTransform.TransformPoint(_local), _button ? "button" : "text"));
                        }
                    }
                }
            }
            return _samples;
        }

        private IEnumerator MeasureCard(Card _card, List<VisPoint> _points, Dictionary<string, int> _hiders, Action<Texture2D> _keep,
            bool _reticle)
        {
            yield return new WaitForEndOfFrame();
            Texture2D _a1 = ScreenCapture.CaptureScreenshotAsTexture();
            yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D _a2 = ScreenCapture.CaptureScreenshotAsTexture();
            Camera _camera = Camera.main;
            var _samples = SamplePoints(_card); // positions at the measured frame
            var _projected = _samples.Select(_s => _camera != null ? _camera.WorldToScreenPoint(_s.world) : Vector3.zero).ToList();

            // The card's face and panel painted flat magenta, drawn exactly where (and in the same canvases as) the card
            // draws them: wherever magenta reaches the screen, the player sees the card. A dark card on a dark table
            // reads as well as a bright one, and anything over the card (3D model, another card, HUD) covers the paint.
            // The card's own 3D extras (a marker pin standing on it, effects) are part of the card, not something
            // hiding it: switched off for the painted frame.
            List<GameObject> _paint = Paint(_card);
            Transform _body = _card.transform.Find("Displacer/Pivot/Scaler/3DModel");
            List<Renderer> _extras = _card.GetComponentsInChildren<Renderer>(false)
                .Where(_r => _r.enabled && _r.transform != _body && !(_r is CanvasRenderer)).ToList();
            _extras.ForEach(_r => _r.enabled = false);
            yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D _c = ScreenCapture.CaptureScreenshotAsTexture();
            _paint.ForEach(_g => { if (_g) Destroy(_g); });
            _extras.ForEach(_r => { if (_r) _r.enabled = true; });

            for (int _k = 0; _k < _samples.Count; _k++)
            {
                Vector3 _screen = _projected[_k];
                var _point = new VisPoint { screen = _screen, part = _samples[_k].part };
                _points.Add(_point);
                if (_screen.z <= 0f || _screen.x < 1f || _screen.y < 1f || _screen.x > _a2.width - 2 || _screen.y > _a2.height - 2)
                {
                    _point.state = "offscreen";
                    continue;
                }
                if (_reticle && Vector2.Distance(_screen, new Vector2(_a2.width / 2f, _a2.height / 2f)) < 9f)
                {
                    _point.state = "reticle"; // the seated reticle dot, drawn over whatever is aimed at
                    continue;
                }
                int _x = (int)_screen.x, _y = (int)_screen.y;
                Color _painted = _c.GetPixel(_x, _y);
                float _change = Diff(_a2.GetPixel(_x, _y), _painted);
                float _noise = Diff(_a1.GetPixel(_x, _y), _a2.GetPixel(_x, _y));
                bool _magenta = _painted.r - _painted.g > 0.25f && _painted.b - _painted.g > 0.25f;
                _point.state = _magenta && _change > Mathf.Max(0.2f, _noise * 2f + 0.05f) ? "visible" : "hidden";
                if (_point.part == "face")
                {
                    _point.reachable = PointerReachesCard(_screen, _card, out string _over);
                    if (_point.reachable == false)
                    {
                        string _key = "hover-taken-by:" + _over;
                        _hiders[_key] = (_hiders.TryGetValue(_key, out int _t) ? _t : 0) + 1;
                    }
                }
                if (_point.part == "button")
                {
                    _point.clickable = ClickReachesVoteButton(_screen, _card, out string _taker);
                    if (_point.clickable == false)
                    {
                        string _key = "click-taken-by:" + _taker;
                        _hiders[_key] = (_hiders.TryGetValue(_key, out int _t) ? _t : 0) + 1;
                    }
                }
                if (_point.state == "hidden" && _camera != null)
                {
                    string _by = WhatCovers(_camera, _screen, _samples[_k].world, _card);
                    _point.cover = _by;
                    _hiders[_by] = (_hiders.TryGetValue(_by, out int _n) ? _n : 0) + 1;
                }
            }
            Destroy(_a1);
            Destroy(_c);
            _keep(_a2);
        }

        // Flat magenta images over the card's front canvas and over every drawn vote-panel graphic (last sibling: on
        // top of the card's own drawing, under whatever is drawn over the card).
        private static List<GameObject> Paint(Card _card)
        {
            var _paint = new List<GameObject>();
            // Every canvas the card draws (front, back, info marks such as the healed pastille, the "Moi" tag), each
            // painted on top of its own content: a card whose role is unknown lies face down (its back up), the side
            // facing the table is under the card's 3D body, so painting it changes nothing on screen. The vote panel
            // is painted graphic by graphic below (its canvas is a 1 x 1 anchor).
            VoteCanvas _panel = _card.GetComponentInChildren<VoteCanvas>(true);
            var _painted = new HashSet<Canvas>();
            foreach (Canvas _canvas in _card.GetComponentsInChildren<Canvas>(false))
            {
                if (_canvas.transform == _card.transform || (_panel != null && _canvas.transform.IsChildOf(_panel.transform)))
                {
                    continue;
                }
                _paint.Add(PaintOver(_canvas.transform));
                _painted.Add(_canvas);
            }
            // Graphics drawn by a canvas not painted above (the card root's 0 x 0 canvas: card effect marks such as the
            // healed pastille, instantiated under cardEffectsParent): painted one by one.
            foreach (Graphic _graphic in _card.GetComponentsInChildren<Graphic>(false))
            {
                if (_graphic.canvas == null || _painted.Contains(_graphic.canvas) || _graphic.name == "VisPaint" ||
                    (_panel != null && _graphic.transform.IsChildOf(_panel.transform)))
                {
                    continue;
                }
                _paint.Add(PaintOver(_graphic.transform));
            }
            VoteCanvas _vote = _card.GetComponentInChildren<VoteCanvas>(true);
            if (_vote != null && _vote.isActiveAndEnabled)
            {
                foreach (Graphic _graphic in PanelGraphics(_vote))
                {
                    _paint.Add(PaintOver(_graphic.transform));
                }
            }
            return _paint;
        }

        private static GameObject PaintOver(Transform _parent)
        {
            var _go = new GameObject("VisPaint", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var _rect = (RectTransform)_go.transform;
            _rect.SetParent(_parent, false);
            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.one;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
            _rect.SetAsLastSibling();
            var _image = _go.GetComponent<Image>();
            _image.color = Color.magenta;
            _image.raycastTarget = false;
            return _go;
        }

        private static IEnumerable<Graphic> PanelGraphics(VoteCanvas _vote)
            => _vote.GetComponentsInChildren<Graphic>(false).Where(_g => _g.isActiveAndEnabled && _g.name != "VisPaint" &&
                _g.color.a >= 0.05f && _g.canvasRenderer.GetInheritedAlpha() >= 0.05f);

        // Centre of what the player looks at for this card: its face and its vote panel.
        private static Vector3 VisualCentre(Card _card)
        {
            var _samples = SamplePoints(_card);
            if (_samples.Count == 0)
            {
                return CardCentre(_card);
            }
            Vector3 _sum = Vector3.zero;
            foreach (var _sample in _samples)
            {
                _sum += _sample.world;
            }
            return _sum / _samples.Count;
        }

        // Hover / unhover through the card's own pointer handlers, then let the hover animations run (real time, the
        // probe keeps time frozen otherwise).
        private IEnumerator SetHovered(Card _card, bool _hovered)
        {
            var _event = new PointerEventData(EventSystem.current);
            if (_hovered)
            {
                _card.OnPointerEnter(_event);
            }
            else
            {
                _card.OnPointerExit(_event);
            }
            Time.timeScale = 1f;
            yield return new WaitForSecondsRealtime(0.8f);
            Time.timeScale = 0f;
        }

        // A click at this screen point (the cursor's, or the seated reticle's: both take the EventSystem's sorted UI
        // raycast) goes to the first UI result that handles clicks: is it this card's vote button? The UI raycast
        // sorts by canvas order before distance, so a canvas BEHIND the card can take the click (the characters bar's
        // portraits, order 0, over the vote panel's -100: 2026-10-08).
        private bool? ClickReachesVoteButton(Vector2 _screen, Card _card, out string _taker)
        {
            _taker = "none";
            VoteCanvas _vote = _card.GetComponentInChildren<VoteCanvas>(true);
            if (EventSystem.current == null || _vote == null || _vote.voteButton == null)
            {
                return null;
            }
            visRaycast.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = _screen }, visRaycast);
            foreach (RaycastResult _hit in visRaycast)
            {
                if (_hit.gameObject == null || !(_hit.module is GraphicRaycaster))
                {
                    continue;
                }
                GameObject _handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(_hit.gameObject);
                if (_handler == null)
                {
                    continue;
                }
                _taker = _handler.name;
                return _handler.transform.IsChildOf(_vote.voteButton.transform) || _vote.voteButton.transform.IsChildOf(_handler.transform);
            }
            return false;
        }

        // Pointing at this screen point (cursor or seated reticle) hovers the first UI result that handles hover: is it
        // this card? A canvas behind the card with a higher sorting order (the characters bar's portraits) took it.
        private bool? PointerReachesCard(Vector2 _screen, Card _card, out string _over)
        {
            _over = "none";
            if (EventSystem.current == null)
            {
                return null;
            }
            visRaycast.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = _screen }, visRaycast);
            foreach (RaycastResult _hit in visRaycast)
            {
                if (_hit.gameObject == null || !(_hit.module is GraphicRaycaster))
                {
                    continue;
                }
                GameObject _handler = ExecuteEvents.GetEventHandler<IPointerEnterHandler>(_hit.gameObject);
                if (_handler == null)
                {
                    continue;
                }
                _over = _handler.name;
                return _handler.transform.IsChildOf(_card.transform);
            }
            return false;
        }

        private static float Diff(Color _a, Color _b) => Mathf.Abs(_a.r - _b.r) + Mathf.Abs(_a.g - _b.g) + Mathf.Abs(_a.b - _b.b);

        private readonly List<RaycastResult> visRaycast = new();

        // Best guess of what covers a hidden point (diagnostic only; the verdict is the pixel test): the first UI /
        // physics hit not part of this card, else the nearest renderer whose bounds the view ray crosses before the card.
        private string WhatCovers(Camera _camera, Vector3 _screen, Vector3 _world, Card _card)
        {
            if (EventSystem.current != null)
            {
                visRaycast.Clear();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = _screen }, visRaycast);
                foreach (RaycastResult _hit in visRaycast)
                {
                    if (_hit.gameObject == null || _hit.gameObject.transform.IsChildOf(_card.transform))
                    {
                        continue;
                    }
                    Card _other = _hit.gameObject.GetComponentInParent<Card>();
                    return _other != null ? "card" : "ui:" + _hit.gameObject.name;
                }
            }

            Ray _ray = _camera.ScreenPointToRay(_screen);
            float _distance = Vector3.Distance(_ray.origin, _world) - 0.05f;
            string _best = "unknown";
            float _bestVolume = float.MaxValue;
            foreach (Renderer _renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!_renderer.enabled || !_renderer.isVisible || _renderer.transform.IsChildOf(_card.transform) ||
                    !_renderer.bounds.IntersectRay(_ray, out float _hitDistance) || _hitDistance >= _distance)
                {
                    continue;
                }
                Vector3 _size = _renderer.bounds.size;
                float _volume = _size.x * _size.y * _size.z;
                if (_volume < _bestVolume)
                {
                    _bestVolume = _volume;
                    Card _owner = _renderer.GetComponentInParent<Card>();
                    // A marker another card carries (the Technomancien's beacon standing on it) is told apart:
                    // "effect:" covers are reported separately (-fx ratios).
                    _best = (_owner == null ? "3d:" : IsCardEffect(_renderer.transform, _owner) ? "effect:" : "card:") + _renderer.name;
                }
            }
            return _best;
        }

        private void SaveAnnotated(Texture2D _shot, List<VisPoint> _points, string _name)
        {
            foreach (VisPoint _p in _points)
            {
                Color _color = _p.state == "visible" ? (_p.part == "face" ? Color.green : Color.cyan) : _p.state == "hidden" ? Color.red : Color.magenta;
                int _cx = Mathf.Clamp((int)_p.screen.x, 1, _shot.width - 2), _cy = Mathf.Clamp((int)_p.screen.y, 1, _shot.height - 2);
                for (int _dx = -1; _dx <= 1; _dx++)
                {
                    for (int _dy = -1; _dy <= 1; _dy++)
                    {
                        _shot.SetPixel(_cx + _dx, _cy + _dy, _color);
                    }
                }
            }
            _shot.Apply(false);
            try
            {
                File.WriteAllBytes(Path.Combine(Journal.OutputDirectory, _name + ".jpg"), _shot.EncodeToJPG(85));
            }
            catch (Exception _exception)
            {
                Journal.Record("vis.error", $"save {_name}: {_exception.Message}");
            }
        }

        // The view as the player sees it, before any measurement marks: vis-<layout>-<view>-clean.jpg.
        private IEnumerator SaveClean(string _name)
        {
            yield return new WaitForEndOfFrame();
            Texture2D _shot = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                File.WriteAllBytes(Path.Combine(Journal.OutputDirectory, _name + ".jpg"), _shot.EncodeToJPG(85));
            }
            catch (Exception _exception)
            {
                Journal.Record("vis.error", $"save {_name}: {_exception.Message}");
            }
            Destroy(_shot);
        }

        // Seated first person: turn the head (the embodied camera's own clamped yaw / pitch) towards a point.
        private string AimHeadAt(Vector3 _target)
        {
            AvatarEmbodiedCamera _embodied = FindFirstObjectByType<AvatarEmbodiedCamera>();
            Camera _camera = Camera.main;
            if (_embodied == null || _camera == null)
            {
                return " aim=none";
            }
            float _yawClamp = GetField<float>(_embodied, "_yawClamp");
            float _pitchClamp = GetField<float>(_embodied, "_pitchClamp");
            float _yaw = GetField<float>(_embodied, "_yaw");
            float _pitch = GetField<float>(_embodied, "_pitch");
            Transform _cameraTransform = _embodied.GetComponent<CinemachineCamera>() != null ? _embodied.transform : _camera.transform;
            Quaternion _seat = _cameraTransform.rotation * Quaternion.Inverse(Quaternion.Euler(_pitch, _yaw, 0f));
            Vector3 _local = Quaternion.Inverse(_seat) * (_target - _cameraTransform.position);
            float _wantYaw = Mathf.Atan2(_local.x, _local.z) * Mathf.Rad2Deg;
            float _wantPitch = -Mathf.Atan2(_local.y, new Vector2(_local.x, _local.z).magnitude) * Mathf.Rad2Deg;
            float _setYaw = Mathf.Clamp(_wantYaw, -_yawClamp, _yawClamp);
            float _setPitch = Mathf.Clamp(_wantPitch, -_pitchClamp, _pitchClamp);
            SetEmbodiedLook(_setYaw, _setPitch);
            return string.Format(CultureInfo.InvariantCulture, " look={0:0.0},{1:0.0}{2}", _wantYaw, _wantPitch,
                Mathf.Approximately(_setYaw, _wantYaw) && Mathf.Approximately(_setPitch, _wantPitch) ? string.Empty : " clamped");
        }

        private static void SetEmbodiedLook(float _yaw, float _pitch)
        {
            AvatarEmbodiedCamera _embodied = FindFirstObjectByType<AvatarEmbodiedCamera>();
            if (_embodied == null)
            {
                return;
            }
            SetField(_embodied, "_yaw", _yaw);
            SetField(_embodied, "_pitch", _pitch);
        }

        private IEnumerator WaitStillCamera()
        {
            Camera _camera = Camera.main;
            if (_camera == null)
            {
                yield break;
            }
            Quaternion _previous = _camera.transform.rotation;
            int _still = 0;
            for (int _frame = 0; _frame < 240 && _still < 3; _frame++)
            {
                yield return null;
                _still = Quaternion.Angle(_previous, _camera.transform.rotation) < 0.01f ? _still + 1 : 0;
                _previous = _camera.transform.rotation;
            }
        }

        private static T GetField<T>(object _target, string _name)
        {
            FieldInfo _field = _target.GetType().GetField(_name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return _field != null ? (T)_field.GetValue(_target) : default;
        }

        private static void SetField(object _target, string _name, object _value)
        {
            _target.GetType().GetField(_name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.SetValue(_target, _value);
        }
    }
}
#endif
