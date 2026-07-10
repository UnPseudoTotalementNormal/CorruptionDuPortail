using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameLogic;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Story 12.3 / Epic 12 (D6) — the endgame static-absence guard (architecture doc §4 census +
    /// §10 DoD). Scans the whole game assembly and FAILS if any project type exposes a static
    /// <c>instance</c>/<c>Instance</c> accessor that is NOT a recorded §4 census survivor.
    ///
    /// <see cref="CompositionRoot"/> is the ONE sanctioned project static, and it is For(nm)-based —
    /// it carries no <c>instance</c>/<c>Instance</c> member at all, so it is not even a candidate here.
    /// Every OTHER singleton is a recorded verify-don't-force exception whose reason is hardcoded in
    /// <see cref="RecordedSurvivors"/>. A NEW singleton added later fails this guard until it is either
    /// removed, injected, or explicitly recorded — the census made EXECUTABLE, not aspirational
    /// (model: <see cref="LeafPocoNoFacadeGuardTests"/>). Third-party assemblies (FMOD / UniTask /
    /// TimeRecorder) are out of scope: the scan targets only the game assembly.
    /// </summary>
    [Category("StaticAbsenceGuard")]
    public class StaticSingletonCensusGuardTests
    {
        // Recorded §4 census survivors keyed by simple type name (the 22 names are unique in the game
        // assembly). Each value is the recorded verify-don't-force reason — adding an entry is a
        // deliberate, reviewed act that keeps the census honest.
        private static readonly Dictionary<string, string> RecordedSurvivors = new()
        {
            // The two God-Object façades — kept as recorded exceptions (12.3 strategy B). Read ONLY by
            // context-less static machinery (W* winning-condition POCOs, TargetUtils) + ChatManager's own
            // NFR5 GetSafeRpcTarget + the network test fixtures. No injection seam; CompositionRoot.For(nm)
            // is the sanctioned indirection the rest of the codebase uses (§4a / §10).
            { "GameManager", "God-Object façade — context-less callers (W*/TargetUtils) + fixtures (§4a/§10)." },
            { "CharacterManager", "God-Object façade — context-less callers + ChatManager NFR5 GetSafeRpcTarget (§4a/§10)." },
            // Replicated singletons NOT de-singletonised by design — served through CompositionRoot.For(nm)
            // (the root forwards to .instance); §4b–§4f.
            { "ChatManager", "replicated singleton (not de-singletonised); root serves it (§4b)." },
            { "RoleTargetSystem", "replicated singleton; root serves it (§4c)." },
            { "BoardManager", "replicated singleton; root serves it (§4d)." },
            { "ChainingManager", "replicated singleton; root serves it (§4e)." },
            { "StatesCanvas", "replicated UI-host singleton; root serves it (§4e)." },
            { "MessageManager", "replicated singleton; root serves it (§4e)." },
            { "LobbyPlayerInfoHolder", "replicated singleton; root serves it (§4e)." },
            { "SelectionFlowService", "POCO singleton (eager new()); root serves it (§4f)." },
            { "FocusManager", "scene singleton; root serves it (§4f)." },
            // Global services / third-party-backed façades — permanent opt-out (no game-loop lifecycle); §4f.
            { "GameAudioManager", "global FMOD audio façade — permanent opt-out (§4f)." },
            { "InputManager", "global Unity-input façade — permanent opt-out (§4f)." },
            { "LobbyManager", "global lobby/Steam service — menu phase, permanent opt-out (§4f)." },
            { "GameAssetHolder", "global asset holder — no game-loop lifecycle (§4f)." },
            // UI / FX presentation singletons — recorded survivors; §4f / §4g.
            { "CardPickerManager", "UI singleton (board picker); §4d/§4g." },
            { "TooltipManager", "UI singleton (tooltip); §4f/§4g." },
            { "NoteManager", "UI singleton (notes); §4f/§4g." },
            { "ArrowManager", "FX singleton (arrows); §4f." },
            { "CardEffectManager", "board FX singleton; §4f." },
            { "BoardCameraManager", "board camera singleton; §4f." },
            { "PowerManager", "power UI/control singleton; §4f." },
        };

        [Test]
        public void NoUnrecordedProjectSingleton_ExposesAStaticInstanceAccessor()
        {
            var offenders = GetGameAssemblyTypes()
                .Where(HasStaticInstanceAccessor)
                .Select(t => t.Name)
                .Where(name => !RecordedSurvivors.ContainsKey(name))
                .Distinct()
                .ToArray();

            CollectionAssert.IsEmpty(offenders,
                $"Unrecorded static singleton(s) in the game assembly: [{string.Join(", ", offenders)}]. " +
                "The despaghettification endgame (§10 DoD) allows ONLY CompositionRoot (For(nm)-based, no " +
                "instance member) plus the recorded §4 census survivors. Remove the singleton, inject it, or — " +
                "if it is a genuine verify-don't-force exception — add it to RecordedSurvivors WITH its reason.");
        }

        [Test]
        public void EveryRecordedSurvivor_StillExistsAndStillExposesItsAccessor()
        {
            // Staleness guard: a whitelisted survivor that was deleted/renamed (or lost its accessor) must
            // be pruned from RecordedSurvivors, else the census lies and hides a future regression.
            Type[] gameTypes = GetGameAssemblyTypes().ToArray();
            var stale = RecordedSurvivors.Keys
                .Where(name =>
                {
                    Type t = gameTypes.FirstOrDefault(x => x.Name == name);
                    return t == null || !HasStaticInstanceAccessor(t);
                })
                .ToArray();

            CollectionAssert.IsEmpty(stale,
                $"Whitelisted survivor(s) no longer exist or no longer expose a static instance accessor: " +
                $"[{string.Join(", ", stale)}]. Prune them from RecordedSurvivors — a stale whitelist hides regressions.");
        }

        [Test]
        public void Guard_Bites_OnSyntheticUnrecordedSingleton()
        {
            // Proves the detector fires. The synthetic type lives in this TEST assembly (the production
            // scan targets the game assembly), so we exercise the detector + whitelist check directly.
            Assert.IsTrue(HasStaticInstanceAccessor(typeof(SyntheticSingleton)),
                "The detector failed to see a static 'instance' field — the mechanism is broken.");
            Assert.IsFalse(RecordedSurvivors.ContainsKey(nameof(SyntheticSingleton)),
                "The synthetic singleton must not be whitelisted — it models an unrecorded regression.");
        }

        // The one sanctioned static (CompositionRoot) must NOT carry an instance/Instance accessor — it
        // resolves through For(nm). This pins that property so a future change can't quietly add one.
        [Test]
        public void CompositionRoot_HasNoStaticInstanceAccessor()
        {
            Assert.IsFalse(HasStaticInstanceAccessor(typeof(CompositionRoot)),
                "CompositionRoot grew a static instance/Instance accessor — it must stay For(nm)-based " +
                "(the one sanctioned static is a resolver, not a singleton). See §4 / §10.");
        }

        private static IEnumerable<Type> GetGameAssemblyTypes()
        {
            Assembly gameAssembly = typeof(GameManager).Assembly;
            try
            {
                return gameAssembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                // A partially-loadable assembly still yields its resolvable types — scan those.
                return e.Types.Where(t => t != null);
            }
        }

        private static bool HasStaticInstanceAccessor(Type type)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (string name in new[] { "instance", "Instance" })
            {
                if (type.GetField(name, flags) != null) return true;
                if (type.GetProperty(name, flags) != null) return true;
            }
            return false;
        }

#pragma warning disable CS0649 // assigned via reflection only; never set in code
        private sealed class SyntheticSingleton
        {
            public static SyntheticSingleton instance;
        }
#pragma warning restore CS0649
    }
}
