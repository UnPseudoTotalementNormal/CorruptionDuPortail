using System.Collections.Generic;
using GameLogic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// Pure coverage for the CLIENT-side <see cref="LocalBarIconRegistry"/> — the local channel the
    /// CharactersBar icon stack merges with the server slice (corruption is its first consumer). Covers the
    /// decidable rows of the spec matrix WITHOUT the engine loop: set/clear/get ordering, replace-by-key,
    /// the change event's fire/no-fire discipline, per-player isolation, and For(nm) isolation between two
    /// NetworkManagers (the "no host/client leak" guarantee).
    /// </summary>
    [Category("PlayerIcons")]
    public class LocalBarIconRegistryTests
    {
        private const string KeyA = "corruption";
        private const string KeyB = "power";
        private const ulong Marked1 = 1UL;
        private const ulong Marked2 = 2UL;

        private readonly List<Object> _temp = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var _object in _temp)
            {
                if (_object != null)
                {
                    Object.DestroyImmediate(_object);
                }
            }
            _temp.Clear();
        }

        private Sprite MakeSprite()
        {
            var _texture = new Texture2D(2, 2);
            _temp.Add(_texture);
            var _sprite = Sprite.Create(_texture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
            _temp.Add(_sprite);
            return _sprite;
        }

        // Inactive GameObject so NetworkManager.Awake (singleton claim / self-destroy) never runs — the
        // component is only ever used here as a dictionary KEY, exactly like production For(nm) resolution.
        private NetworkManager MakeNetworkManager()
        {
            var _go = new GameObject("nm-for-test");
            _go.SetActive(false);
            _temp.Add(_go);
            return _go.AddComponent<NetworkManager>();
        }

        // ---- instance logic -----------------------------------------------------------------------

        [Test]
        public void EmptyByDefault()
        {
            var _registry = new LocalBarIconRegistry();
            Assert.IsEmpty(_registry.GetIconsFor(Marked1));
        }

        [Test]
        public void SetIcon_ThenGet_ReturnsSprite()
        {
            var _registry = new LocalBarIconRegistry();
            Sprite _sprite = MakeSprite();

            _registry.SetIcon(Marked1, KeyA, _sprite);

            var _icons = _registry.GetIconsFor(Marked1);
            Assert.AreEqual(1, _icons.Count);
            Assert.AreSame(_sprite, _icons[0]);
        }

        [Test]
        public void Icons_KeepRegistrationOrder()
        {
            var _registry = new LocalBarIconRegistry();
            Sprite _first = MakeSprite();
            Sprite _second = MakeSprite();

            _registry.SetIcon(Marked1, KeyA, _first);
            _registry.SetIcon(Marked1, KeyB, _second);

            var _icons = _registry.GetIconsFor(Marked1);
            Assert.AreEqual(2, _icons.Count);
            Assert.AreSame(_first, _icons[0]);
            Assert.AreSame(_second, _icons[1]);
        }

        [Test]
        public void SetIcon_SameKey_ReplacesInPlace()
        {
            var _registry = new LocalBarIconRegistry();
            Sprite _old = MakeSprite();
            Sprite _new = MakeSprite();

            _registry.SetIcon(Marked1, KeyA, _old);
            _registry.SetIcon(Marked1, KeyA, _new);

            var _icons = _registry.GetIconsFor(Marked1);
            Assert.AreEqual(1, _icons.Count, "Replacing a key must not append a second entry.");
            Assert.AreSame(_new, _icons[0]);
        }

        [Test]
        public void ClearIcon_RemovesIt()
        {
            var _registry = new LocalBarIconRegistry();
            _registry.SetIcon(Marked1, KeyA, MakeSprite());

            _registry.ClearIcon(Marked1, KeyA);

            Assert.IsEmpty(_registry.GetIconsFor(Marked1));
        }

        [Test]
        public void SetIcon_NullSprite_ClearsKey()
        {
            var _registry = new LocalBarIconRegistry();
            _registry.SetIcon(Marked1, KeyA, MakeSprite());

            _registry.SetIcon(Marked1, KeyA, null);

            Assert.IsEmpty(_registry.GetIconsFor(Marked1));
        }

        [Test]
        public void Players_AreIsolated()
        {
            var _registry = new LocalBarIconRegistry();
            _registry.SetIcon(Marked1, KeyA, MakeSprite());

            Assert.AreEqual(1, _registry.GetIconsFor(Marked1).Count);
            Assert.IsEmpty(_registry.GetIconsFor(Marked2));
        }

        // ---- change-event discipline --------------------------------------------------------------

        [Test]
        public void Event_Fires_OnAdd_Change_And_Clear()
        {
            var _registry = new LocalBarIconRegistry();
            int _fired = 0;
            _registry.onLocalIconsChanged += () => _fired++;

            _registry.SetIcon(Marked1, KeyA, MakeSprite());   // add
            _registry.SetIcon(Marked1, KeyA, MakeSprite());   // change (different sprite)
            _registry.ClearIcon(Marked1, KeyA);               // clear

            Assert.AreEqual(3, _fired);
        }

        [Test]
        public void Event_DoesNotFire_OnUnchanged_Or_UnknownClear()
        {
            var _registry = new LocalBarIconRegistry();
            Sprite _sprite = MakeSprite();
            _registry.SetIcon(Marked1, KeyA, _sprite);

            int _fired = 0;
            _registry.onLocalIconsChanged += () => _fired++;

            _registry.SetIcon(Marked1, KeyA, _sprite); // same sprite → no change
            _registry.ClearIcon(Marked1, KeyB);        // unknown key → no change
            _registry.ClearIcon(Marked2, KeyA);        // unknown player → no change

            Assert.AreEqual(0, _fired);
        }

        // ---- For(nm) resolution -------------------------------------------------------------------

        [Test]
        public void For_Null_ReturnsNull()
        {
            Assert.IsNull(LocalBarIconRegistry.For(null));
        }

        [Test]
        public void For_SameManager_ReturnsSameInstance()
        {
            NetworkManager _nm = MakeNetworkManager();
            Assert.AreSame(LocalBarIconRegistry.For(_nm), LocalBarIconRegistry.For(_nm));
        }

        [Test]
        public void For_DifferentManagers_AreIsolated()
        {
            NetworkManager _nmA = MakeNetworkManager();
            NetworkManager _nmB = MakeNetworkManager();

            LocalBarIconRegistry _a = LocalBarIconRegistry.For(_nmA);
            LocalBarIconRegistry _b = LocalBarIconRegistry.For(_nmB);
            Assert.AreNotSame(_a, _b);

            _a.SetIcon(Marked1, KeyA, MakeSprite());
            Assert.AreEqual(1, _a.GetIconsFor(Marked1).Count);
            Assert.IsEmpty(_b.GetIconsFor(Marked1), "A different NetworkManager's channel must not see A's icons.");
        }
    }
}
