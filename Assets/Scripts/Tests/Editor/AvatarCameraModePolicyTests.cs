using Avatars;
using GameLogic;
using GameLogic.GameStates;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor.Avatars
{
    /// <summary>
    /// Story 13.3 — the GOLDEN TABLE pinning the pure state → camera-mode mapping
    /// (<see cref="AvatarCameraModePolicy"/>). The policy is type-keyed, so a future state
    /// REORDER in the GameManager dictionary is a no-op and a state RENAME breaks compilation;
    /// a MIS-MAP breaks the single offending row below (one assert per row names the culprit).
    ///
    /// Pure policy → states are instantiated with <c>ScriptableObject.CreateInstance&lt;T&gt;()</c>
    /// and resolved with ZERO network setup (no NetworkManager, no scene). SOs are destroyed in
    /// the per-resolve helper so nothing leaks across the EditMode session.
    /// </summary>
    public class AvatarCameraModePolicyTests
    {
        private static CameraMode Resolve<T>() where T : GameState
        {
            T _state = ScriptableObject.CreateInstance<T>();
            try
            {
                return AvatarCameraModePolicy.ResolveMode(_state);
            }
            finally
            {
                Object.DestroyImmediate(_state);
            }
        }

        [Test]
        public void LobbyState_MapsTo_FreeRoam()
        {
            Assert.AreEqual(CameraMode.FreeRoam, Resolve<LobbyState>());
        }

        [Test]
        public void VoteState_MapsTo_Embodied()
        {
            Assert.AreEqual(CameraMode.Embodied, Resolve<VoteState>());
        }

        [Test]
        public void AwakeningState_MapsTo_Board()
        {
            Assert.AreEqual(CameraMode.Board, Resolve<AwakeningState>());
        }

        [Test]
        public void ChainingState_MapsTo_Board()
        {
            Assert.AreEqual(CameraMode.Board, Resolve<ChainingState>());
        }

        [Test]
        public void VoteRecapState_MapsTo_Embodied()
        {
            // Recap is a DAY phase — seated with the other players visible (Embodied), like the vote.
            Assert.AreEqual(CameraMode.Embodied, Resolve<VoteRecapState>());
        }

        [Test]
        public void NullState_MapsTo_Board()
        {
            // null (no current state yet — early boot) keeps the safe board presentation (NFR1).
            Assert.AreEqual(CameraMode.Board, AvatarCameraModePolicy.ResolveMode(null));
        }
    }
}
