using System;
using GameLogic;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime.Executors
{
    /// <summary>
    /// Removes a PRIVATE power icon from a player's CharactersBar thumbnail. Same For(nm) resolution as
    /// <see cref="AddPlayerIconExecutor"/>; an unknown marker is a silent no-op on the manager side.
    /// </summary>
    public sealed class RemovePlayerIconExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(RemovePlayerIcon);

        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (RemovePlayerIcon)effect;
            PlayerIconManager.For(runtime.NetworkManager)
                ?.RemoveIcon(e.IconId, (ulong)e.MarkedSlot, (ulong)e.ViewerSlot);
        }
    }
}
