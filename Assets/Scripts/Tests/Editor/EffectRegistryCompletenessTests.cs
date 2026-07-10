using System;
using System.Collections.Generic;
using System.Linq;
using Characters.Powers.Runtime;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Powers v2 — completeness guard over the REAL effect-dispatch registry (the safety net the v2 spec
    /// promised for Fork 2). EffectDispatcherTests proves the routing MECHANISM with a fake executor; this
    /// proves the actual PowerDispatcherHost registry COVERS every effect a decision can emit. A new
    /// EffectDescriptor wired into a decision but missing its executor now fails HERE (EditMode) instead of
    /// throwing a runtime NotSupportedException mid-power in a live, server-authoritative game.
    /// </summary>
    [Category("PowerDecision")]
    public class EffectRegistryCompletenessTests
    {
        // EffectDescriptor subtypes that NO decision emits and NO executor handles (producer-less dead
        // data). The v1-legacy bricks that used to sit here were pruned, so this set is now EMPTY — every
        // remaining descriptor must have an executor. Add a name here only if you intentionally introduce a
        // producer-less descriptor; removing a type without adding its executor (or vice-versa) fails one of
        // the two tests below by design, so the allowlist cannot silently rot.
        private static readonly HashSet<string> KnownProducerless = new HashSet<string>();

        private static IEnumerable<Type> AllDescriptorTypes() =>
            typeof(EffectDescriptor).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && t != typeof(EffectDescriptor)
                            && typeof(EffectDescriptor).IsAssignableFrom(t));

        [Test]
        public void EveryDescriptor_HasExecutor_OrIsKnownProducerless()
        {
            var dispatcher = PowerDispatcherHost.Dispatcher;
            var gaps = AllDescriptorTypes()
                .Where(t => !dispatcher.HasExecutorFor(t) && !KnownProducerless.Contains(t.Name))
                .Select(t => t.Name)
                .OrderBy(n => n)
                .ToList();

            Assert.IsEmpty(gaps,
                "EffectDescriptor type(s) with no registered IEffectExecutor: " + string.Join(", ", gaps) +
                ". Add an executor (it self-registers via PowerDispatcherHost) or, if the descriptor is " +
                "intentionally producer-less, add it to KnownProducerless with a reason.");
        }

        [Test]
        public void KnownProducerlessList_StaysHonest()
        {
            var dispatcher = PowerDispatcherHost.Dispatcher;
            var allNames = new HashSet<string>(AllDescriptorTypes().Select(t => t.Name));

            // No typos / stale entries: every allowlisted name is a real EffectDescriptor subtype.
            var unknown = KnownProducerless.Where(n => !allNames.Contains(n)).OrderBy(n => n).ToList();
            Assert.IsEmpty(unknown,
                "KnownProducerless names that are not EffectDescriptor subtypes (typo or deleted type): " +
                string.Join(", ", unknown));

            // No hiding a real executor: an allowlisted type must genuinely have NO executor. If one was
            // added, remove the type from the allowlist so coverage is asserted, not excused.
            var nowCovered = AllDescriptorTypes()
                .Where(t => KnownProducerless.Contains(t.Name) && dispatcher.HasExecutorFor(t))
                .Select(t => t.Name)
                .OrderBy(n => n)
                .ToList();
            Assert.IsEmpty(nowCovered,
                "KnownProducerless type(s) that now HAVE an executor — remove them from the allowlist: " +
                string.Join(", ", nowCovered));
        }
    }
}
