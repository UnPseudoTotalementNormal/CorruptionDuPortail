using System;
using GameLogic;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime.Executors
{
    /// <summary>
    /// Places a PRIVATE power icon on a player's CharactersBar thumbnail. Reaches the manager through
    /// <see cref="PlayerIconManager.For"/> rather than <c>CompositionRoot.For(...)</c> like its neighbours:
    /// PlayerIconManager is a born-clean For(nm)-registry manager with no composition-root entry (spec
    /// §Design Notes — documented on purpose so it is not "corrected" back).
    /// Slots are LOGICAL and map 1:1 onto clientIds here, exactly like <see cref="NewTargetingExecutor"/>.
    /// </summary>
    public sealed class AddPlayerIconExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(AddPlayerIcon);

        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (AddPlayerIcon)effect;
            PlayerIconManager.For(runtime.NetworkManager)
                ?.AddIcon(e.IconId, (ulong)e.MarkedSlot, (ulong)e.ViewerSlot, e.Lifetime);
        }
    }
}
