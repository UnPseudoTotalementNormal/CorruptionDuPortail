using System;
using Characters.Powers.Runtime;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Powers v2 — the effect-dispatch registry (Fork 2). Proves the decentralised routing mechanism
    /// purely (a fake executor, no NGO): routes by concrete descriptor type, throws on an unregistered
    /// effect. The real per-effect executors are proven end-to-end by PlayMode goldens later.
    /// </summary>
    [Category("PowerDecision")]
    public class EffectDispatcherTests
    {
        private sealed class FakeRevealExecutor : IEffectExecutor
        {
            public Type DescriptorType => typeof(RevealInfo);
            public int Calls;
            public EffectDescriptor Last;
            public void Execute(EffectDescriptor effect, EffectRuntime runtime) { Calls++; Last = effect; }
        }

        [Test]
        public void Dispatch_RoutesByConcreteType()
        {
            var fake = new FakeRevealExecutor();
            var dispatcher = new EffectDispatcher(new IEffectExecutor[] { fake });
            var reveal = new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true);

            dispatcher.Dispatch(new EffectDescriptor[] { reveal }, null);

            Assert.AreEqual(1, fake.Calls);
            Assert.AreEqual(reveal, fake.Last);
        }

        [Test]
        public void Dispatch_ThrowsOnUnregisteredEffect()
        {
            var dispatcher = new EffectDispatcher(new IEffectExecutor[0]);
            Assert.Throws<NotSupportedException>(() =>
                dispatcher.Dispatch(new EffectDescriptor[] { new CorruptPlayer(5) }, null));
        }

        [Test]
        public void HasExecutorFor_ReflectsRegistration()
        {
            var dispatcher = new EffectDispatcher(new IEffectExecutor[] { new FakeRevealExecutor() });
            Assert.IsTrue(dispatcher.HasExecutorFor(typeof(RevealInfo)));
            Assert.IsFalse(dispatcher.HasExecutorFor(typeof(CorruptPlayer)));
        }
    }
}
