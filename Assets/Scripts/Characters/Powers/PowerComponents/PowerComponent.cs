using System;
using ChatSystem;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine.Assertions;

namespace Characters.Powers.PowerComponents
{
    public abstract class PowerComponent : NetworkBehaviour
    {
        public FixedString32Bytes componentName;
        public FixedString512Bytes description;
        
        protected Power power;
        // Story 7.1 lane C: CharacterManager resolved once in OnNetworkSpawn (via the composition
        // root), consumed by this base and every concrete component instead of the GameManager hub-hop.
        protected CharacterManager characterManager;
        // Story 10.1 lane C: the chat manager, same seam. Null-tolerant (no Assert) — not every
        // component chats; chatting ones always have one in production and in their own harnesses.
        protected ChatManager chatManager;
        protected Character ownerCharacter => characterManager.GetCharacter(ownerClientId, false);
        protected ulong ownerClientId => power.ownerClientId.Value;
        protected virtual void Awake()
        {
            power = GetComponent<Power>();
            power.powerComponents.Add(this);
            Assert.IsNotNull(power, "PowerComponent must be attached to a GameObject with a Power component");
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            characterManager = GameLogic.CompositionRoot.For(NetworkManager).CharacterManager;
            Assert.IsNotNull(characterManager,
                "PowerComponent.characterManager unresolved — CompositionRoot.For(NetworkManager) returned no CharacterManager.");
            chatManager = GameLogic.CompositionRoot.For(NetworkManager).ChatManager;
            Init();
        }

        protected abstract void Init();
        
        public virtual bool CanUsePower()
        {
            return true;
        }
    }
}