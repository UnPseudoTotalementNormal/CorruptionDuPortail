using Avatars;
using Characters.Powers;
using NUnit.Framework;
using Presentation.Chains;
using UnityEditor;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// The artist's props (T33). What breaks without a compile error: a power object the reticle cannot hit or the bar
    /// squashes, a mesh the candle or the chains cannot read in a player build, a chair that would fly off with the cat.
    /// </summary>
    public class PropsAssetsTests
    {
        private const int BoardClickableLayer = 7;
        private const string PowersDir = "Assets/Prefabs/Powers";
        private const string ObjectsDir = "Assets/Prefabs/PowersBar/3DModels";

        // Still the default model: their objects are not delivered yet.
        private static readonly string[] AwaitingTheirObject = { "CardsShuffling" };

        // The role -> object rows of the artist's asset tracker (T33, Corrompu_Suivi_Assets_3D.xlsx): the game designer's
        // choice, not ours. A new tracker means new rows here, then Tools > Props > Rebuild assets.
        private static readonly (string Power, string Object)[] TrackerRows =
        {
            ("TargetedCorruption", "PowerObject_Corruptor"), // Occult Mage n°1: two corruptors
            ("EmbraceOfShadows", "PowerObject_ArcaneCube"), // Occult Mage n°2: red arcane cube
            ("ChainedByTheShadows", "PowerObject_ChainPile"), // Abyss: chain on the floor
            ("Omniscience", "PowerObject_Totem"), // Robot: MissingNo-like glitch
            ("DroolyHealing", "PowerObject_CrystalBall"), // Gloubi: magic ball
            ("Blessing", "PowerObject_LeafHeart"), // Dryad: leaf heart
            ("PersonalBeacons", "PowerObject_BeaconRobot"), // Technomancer: one-wheeled robot with beacons
            ("LackOfAffection", "PowerObject_TeddyBear"), // Orphan: teddy bear
            ("Reincarnation", "PowerObject_PurpleHand"), // Incomplete: divine purple hand
            ("BoundByInk", "PowerObject_Bell"), // Doorman's bell, Messenger's animation
            ("CursedVision", "PowerObject_CrystalSkull"), // Repentant: crystal skull
            ("ClandestineObservation", "PowerObject_Sniper"), // Bounty Hunter: sniper rifle
        };

        [Test]
        public void EachRolesPower_HasTheObjectOfTheArtistsTracker()
        {
            foreach ((string _powerName, string _objectName) in TrackerRows)
            {
                var _power = AssetDatabase.LoadAssetAtPath<GameObject>($"{PowersDir}/{_powerName}.prefab").GetComponentInChildren<Power>(true);
                Assert.IsNotNull(_power.power3DObjectPrefab, $"{_powerName} has no 3D object");
                Assert.AreEqual(_objectName, _power.power3DObjectPrefab.name, $"{_powerName}: the tracker gives it {_objectName}");
            }
        }

        [Test]
        public void EveryActivePower_HasItsObject_ExceptTheOnesNotDelivered()
        {
            foreach (string _guid in AssetDatabase.FindAssets("t:Prefab", new[] { PowersDir }))
            {
                string _path = AssetDatabase.GUIDToAssetPath(_guid);
                string _name = System.IO.Path.GetFileNameWithoutExtension(_path);
                var _power = AssetDatabase.LoadAssetAtPath<GameObject>(_path).GetComponentInChildren<Power>(true);
                if (_power == null || _power.isPassive)
                {
                    continue;
                }
                if (System.Array.IndexOf(AwaitingTheirObject, _name) >= 0)
                {
                    Assert.IsNull(_power.power3DObjectPrefab, $"{_name} got its object: take it off the waiting list");
                }
                else
                {
                    Assert.IsNotNull(_power.power3DObjectPrefab, $"{_name} has no 3D object");
                }
            }
        }

        [Test]
        public void TechnomancerBeacon_IsTheArtistsModel_OffUntilItsTargetIsCorrupted()
        {
            var _prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/CardEffects/BeaconEffect.prefab");
            var _effect = new SerializedObject(_prefab.GetComponentInChildren<Board.Components.CardEffectTechnoBeacon>(true));
            var _renderer = _effect.FindProperty("targetRenderer").objectReferenceValue as MeshRenderer;
            Assert.IsNotNull(_renderer, "targetRenderer not wired");
            Assert.AreEqual("Beacon", _renderer.GetComponent<MeshFilter>().sharedMesh.name, "the artist's beacon, not the placeholder");
            var _off = (Material)_effect.FindProperty("notCorruptedMaterial").objectReferenceValue;
            var _lit = (Material)_effect.FindProperty("corruptedMaterial").objectReferenceValue;
            Assert.IsFalse(_off.IsKeywordEnabled("_EMISSION"), "healthy target: the beacon is off (asset tracker)");
            Assert.IsTrue(_lit.IsKeywordEnabled("_EMISSION"), "corrupted target: the beacon lights up red");
        }

        [Test]
        public void EveryPowerObject_CanBeClickedAndKeepsItsSizeOnTheBar()
        {
            foreach (string _guid in AssetDatabase.FindAssets("PowerObject_ t:Prefab", new[] { ObjectsDir }))
            {
                var _prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(_guid));
                Assert.AreEqual(Vector3.one, _prefab.transform.localScale, $"{_prefab.name}: PowerBarObject3D resets the root's scale");
                Assert.AreEqual(0f, Quaternion.Angle(Quaternion.Euler(-90f, 0f, 0f), _prefab.transform.localRotation), 0.01f, $"{_prefab.name}: undoes the lying canvas of the power bar");
                var _collider = _prefab.GetComponentInChildren<Collider>(true);
                Assert.IsNotNull(_collider, $"{_prefab.name} has no collider");
                Assert.AreEqual(BoardClickableLayer, _collider.gameObject.layer, $"{_prefab.name}: the reticle only sees layer 3DPhysical");
                foreach (Renderer _renderer in _prefab.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material _material in _renderer.sharedMaterials)
                    {
                        Assert.IsNotNull(_material, $"{_prefab.name}/{_renderer.name}: material slot not remapped");
                    }
                    var _filter = _renderer.GetComponent<MeshFilter>();
                    Assert.IsTrue(_filter != null && _filter.sharedMesh.isReadable, $"{_prefab.name}: the slime of a copied power emits from a readable mesh");
                }
            }
        }

        [Test]
        public void Chair_IsNextToTheCatsVisual_NotInIt()
        {
            var _avatar = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Avatars/PlayerAvatar.prefab");
            var _chair = new SerializedObject(_avatar.GetComponent<PlayerAvatar>()).FindProperty("_chair").objectReferenceValue as GameObject;
            Assert.IsNotNull(_chair, "PlayerAvatar._chair not wired");
            Assert.AreEqual(_avatar.transform, _chair.transform.parent, "under the root: it stays on the seat when the chained cat flies off");
            Assert.IsNull(_chair.GetComponentInChildren<Collider>(true), "a collider would take the clicks on the cat");
        }

        [Test]
        public void Chair_IsHiddenOnlyInTheFreeRoamingLobby()
        {
            Assert.IsFalse(AvatarVisibilityController.ResolveChairShown(CameraMode.FreeRoam));
            Assert.IsTrue(AvatarVisibilityController.ResolveChairShown(CameraMode.Embodied));
            Assert.IsTrue(AvatarVisibilityController.ResolveChairShown(CameraMode.Board));
        }

        [Test]
        public void ChainLink_IsReadableAndWired()
        {
            var _chains = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Avatars/PlayerAvatar.prefab").GetComponent<CatChains>();
            var _link = new SerializedObject(_chains).FindProperty("_linkMesh").objectReferenceValue as Mesh;
            Assert.IsNotNull(_link, "CatChains._linkMesh not wired");
            Assert.IsTrue(_link.isReadable, "laid on the CPU, also in a player build");
            Assert.Greater(_link.uv.Length, 0, "its texture needs the file's UVs");
        }

        [Test]
        public void CandleModel_HasItsPartsAndIsReadable()
        {
            var _candle = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Props/Candle.prefab");
            bool _wax = false, _wick = false;
            foreach (MeshFilter _filter in _candle.GetComponentsInChildren<MeshFilter>(true))
            {
                _wax |= _filter.name == "Candle" && _filter.sharedMesh.isReadable;
                _wick |= _filter.name == "Bit";
            }
            Assert.IsTrue(_wax, "the wax melts on the CPU: readable, also in a player build");
            Assert.IsTrue(_wick, "VoteCandle finds the wick by this name");
        }
    }
}
