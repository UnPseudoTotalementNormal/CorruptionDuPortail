using System;
using GameLogic;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime.Executors
{
    public sealed class SetEliminatedExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(SetEliminated);
        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (SetEliminated)effect;
            CompositionRoot.For(runtime.NetworkManager).CharacterManager
                .GetCharacter((ulong)e.Slot, false).isEliminated.Value = true;
            Diagnostics.Logs.GameLog.Guarded(() => Diagnostics.Logs.GameLog.Info("game", "game.player.eliminated", "seat", (ulong)e.Slot,
                "by", runtime.ViewerRelay is Power _power ? _power.powerName.ToString() : null));
        }
    }
}
