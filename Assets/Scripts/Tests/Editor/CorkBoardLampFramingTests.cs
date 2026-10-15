using FX;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// Board task T11: the night lamp on the cork board is a framing projector. It must light the whole board and
    /// nothing around it (the table in front, what sits past its edges), spread its light by the profile, not by the
    /// spot's own fall-off, and fade out softly before the edges instead of drawing a lit rectangle. The board is the scene's: a unit cube scaled 19.62 × 2.94 × 0.12, tilted 65°.
    /// </summary>
    public class CorkBoardLampFramingTests
    {
        private GameObject _board;
        private Bounds _bounds;
        private CorkBoardLampFraming.Profile _profile;

        [SetUp]
        public void SetUp()
        {
            _board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _board.transform.SetPositionAndRotation(new Vector3(0f, -14.38f, 6.25f), Quaternion.Euler(65f, 0f, 0f));
            _board.transform.localScale = new Vector3(19.62f, 2.94f, 0.12f);
            _bounds = _board.GetComponent<MeshFilter>().sharedMesh.bounds;
            _profile = CorkBoardLampFraming.Profile.Default;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_board);
        }

        private CorkBoardLampFraming.Framing Frame(float _distance = 10f) =>
            CorkBoardLampFraming.Frame(_board.transform, _bounds, _distance, _profile.spill);

        /// <summary>Light reaching world point <paramref name="_point"/> through the cookie (0 = none).</summary>
        private float LightAt(in CorkBoardLampFraming.Framing _framing, Vector3 _point)
        {
            Vector3 _local = Quaternion.Inverse(_framing.Rotation) * (_point - _framing.Position);
            if (_local.z <= 0f)
            {
                return 0f;
            }
            float _tanX = _local.x / _local.z;
            float _tanY = _local.y / _local.z;
            if (Mathf.Abs(_tanX) > _framing.TanHalfAngle || Mathf.Abs(_tanY) > _framing.TanHalfAngle)
            {
                return 0f;
            }
            return CorkBoardLampFraming.CookieValue(_framing, _profile, _tanX, _tanY);
        }

        private Vector3 OnFace(float _u, float _v)
        {
            Transform _t = _board.transform;
            return _t.TransformPoint(new Vector3(_u * 0.5f, _v * 0.5f, -0.5f));
        }

        [Test]
        public void Light_SitsOnTheBoardNormal_LookingAtIt()
        {
            CorkBoardLampFraming.Framing _framing = Frame();
            Vector3 _normal = -_board.transform.forward;
            Vector3 _toLight = _framing.Position - OnFace(0f, 0f);
            Assert.AreEqual(10f, _toLight.magnitude, 1e-3f);
            Assert.Greater(Vector3.Dot(_toLight.normalized, _normal), 0.9999f);
            Assert.Greater(Vector3.Dot(_framing.Rotation * Vector3.forward, -_normal), 0.9999f);
            Assert.Greater(Vector3.Dot(_framing.Rotation * Vector3.up, _board.transform.up), 0.9999f);
        }

        [Test]
        public void TheWholeBoard_IsLit_UpToItsBorder()
        {
            CorkBoardLampFraming.Framing _framing = Frame();
            for (float _u = -0.9f; _u <= 0.9f; _u += 0.1f)
            {
                for (float _v = -0.7f; _v <= 0.7f; _v += 0.35f)
                {
                    Assert.Greater(LightAt(_framing, OnFace(_u, _v)), 0f, $"board point u={_u} v={_v} is dark");
                }
            }
        }

        [Test]
        public void WithoutSpill_NothingAroundTheBoard_IsLit()
        {
            _profile.spill = 0f;
            CorkBoardLampFraming.Framing _framing = Frame();
            // Just past each edge, in the board's plane: the table it stands on, the air at its ends.
            Assert.AreEqual(0f, LightAt(_framing, OnFace(0f, -1.02f)), "below the bottom edge");
            Assert.AreEqual(0f, LightAt(_framing, OnFace(0f, 1.02f)), "above the top edge");
            Assert.AreEqual(0f, LightAt(_framing, OnFace(1.01f, 0f)), "past the right end");
            Assert.AreEqual(0f, LightAt(_framing, OnFace(-1.01f, 0f)), "past the left end");
            // The table top (y = -15.0) in front of the board, from the players up to its bottom edge (z ≈ 4.9; past it
            // the table is under the board).
            for (float _z = -8f; _z <= 4.85f; _z += 0.05f)
            {
                for (float _x = -11f; _x <= 11f; _x += 1f)
                {
                    Assert.AreEqual(0f, LightAt(_framing, new Vector3(_x, -15f, _z)), $"table at x={_x} z={_z} is lit");
                }
            }
        }

        /// <summary>Irradiance on the board's face ∝ cookie · d / r³ (inverse square + slant).</summary>
        private float Irradiance(in CorkBoardLampFraming.Framing _framing, float _u, float _v)
        {
            Vector3 _point = OnFace(_u, _v);
            float _r = Vector3.Distance(_framing.Position, _point);
            return LightAt(_framing, _point) * _framing.Distance / (_r * _r * _r);
        }

        [Test]
        public void TheBoard_IsLitByTheProfile_NotByTheSpotFallOff()
        {
            CorkBoardLampFraming.Framing _framing = Frame();
            float _centre = Irradiance(_framing, 0f, 0f);
            float _shapeCentre = CorkBoardLampFraming.Shape(_framing, _profile, 0f, 0f);
            foreach ((float _u, float _v) in new[] { (0.5f, 0f), (0.85f, 0.2f), (-0.7f, -0.5f), (0.1f, 0.8f) })
            {
                float _expected = CorkBoardLampFraming.Shape(_framing, _profile, _u * _framing.HalfLength,
                    _v * _framing.HalfHeight) / _shapeCentre;
                Assert.AreEqual(_expected, Irradiance(_framing, _u, _v) / _centre, 0.01f, $"u={_u} v={_v}");
            }
            Assert.Greater(Irradiance(_framing, 0f, 0.3f), Irradiance(_framing, 0f, -0.3f), "the top is brighter");
        }

        [Test]
        public void TheLight_FadesOutBeforeTheEdges_WithNoStep()
        {
            _profile.spill = 0f;
            CorkBoardLampFraming.Framing _framing = Frame();
            float _peak = Irradiance(_framing, 0f, 0f);
            // No frame: almost nothing left right at the outline.
            Assert.Less(Irradiance(_framing, 0.995f, 0f), 0.02f * _peak, "right end");
            Assert.Less(Irradiance(_framing, 0f, 0.98f), 0.02f * _peak, "top edge");
            Assert.Less(Irradiance(_framing, 0f, -0.98f), 0.02f * _peak, "bottom edge");
            // A gradual fade: from the centre to each edge, no jump between two points 1 % of the way apart.
            foreach ((float _du, float _dv) in new[] { (1f, 0f), (0f, 1f), (0f, -1f), (0.7f, 0.7f) })
            {
                float _previous = Irradiance(_framing, 0f, 0f);
                for (float _s = 0.01f; _s <= 1f; _s += 0.01f)
                {
                    float _current = Irradiance(_framing, _du * _s, _dv * _s);
                    Assert.Less(Mathf.Abs(_current - _previous), 0.06f * _peak, $"step at s={_s} toward ({_du}, {_dv})");
                    _previous = _current;
                }
            }
        }

        [Test]
        public void Spill_LetsTheLightReachJustPastTheEdges()
        {
            _profile.spill = 0.5f;
            CorkBoardLampFraming.Framing _framing =
                CorkBoardLampFraming.Frame(_board.transform, _bounds, 10f, _profile.spill);
            // Half the spill past the bottom edge, in the board's plane: still some light.
            float _halfway = 1f + 0.25f / _framing.HalfHeight;
            Assert.Greater(LightAt(_framing, OnFace(0f, -_halfway)), 0f);
            // Past the spill: none.
            float _beyond = 1f + 0.52f / _framing.HalfHeight;
            Assert.AreEqual(0f, LightAt(_framing, OnFace(0f, -_beyond)));
            Assert.AreEqual(0f, LightAt(_framing, OnFace(1f + 0.52f / _framing.HalfLength, 0f)));
            // Never past the top edge: that light would fly over the board onto whoever sits behind it.
            Assert.AreEqual(0f, LightAt(_framing, OnFace(0f, 1.01f)));
        }

        [Test]
        public void DefaultHalo_StaysAtTheFootOfTheBoard()
        {
            CorkBoardLampFraming.Framing _framing =
                CorkBoardLampFraming.Frame(_board.transform, _bounds, 10f, _profile.spill);
            Assert.Greater(_profile.spill, 0f, "the chosen look (variant C) spills a little onto the table");
            // Lit just in front of the board's foot (its bottom edge touches the table at z ≈ 4.9)...
            Assert.Greater(LightAt(_framing, new Vector3(0f, -15f, 4.7f)), 0f);
            // ...and nowhere further on the table.
            for (float _z = -8f; _z <= 4.1f; _z += 0.05f)
            {
                for (float _x = -11f; _x <= 11f; _x += 1f)
                {
                    Assert.AreEqual(0f, LightAt(_framing, new Vector3(_x, -15f, _z)), $"table at x={_x} z={_z} is lit");
                }
            }
        }

        [Test]
        public void Intensity_GivesTheWantedBrightness_AtAnyDistance()
        {
            foreach (float _distance in new[] { 6f, 10f, 16f })
            {
                CorkBoardLampFraming.Framing _framing = Frame(_distance);
                Texture2D _cookie = CorkBoardLampFraming.BuildCookie(_framing, _profile, 64, out float _centre);
                Object.DestroyImmediate(_cookie);
                float _intensity = CorkBoardLampFraming.Intensity(1.2f, _distance, _centre);
                Assert.AreEqual(1.2f, _intensity * _centre / (_distance * _distance), 1e-4f);
                Assert.That(_centre, Is.InRange(0.01f, 1f));
            }
        }

        [Test]
        public void TheBoardCorners_StayInsideTheCone()
        {
            CorkBoardLampFraming.Framing _framing =
                CorkBoardLampFraming.Frame(_board.transform, _bounds, 10f, _profile.spill);
            float _reachX = _framing.HalfLength + _profile.spill;
            float _reachY = _framing.HalfHeight + _profile.spill;
            float _cornerTan = Mathf.Sqrt(_reachX * _reachX + _reachY * _reachY) / _framing.Distance;
            Assert.Less(_cornerTan, _framing.TanHalfAngle, "a corner outside the circle would be cut by the cone");
        }
    }
}
