using UnityEngine;

namespace KitchenChaos.Multiplayer {
    public sealed class MultiplayerAssets : ScriptableObject {
        public KitchenObjectSO[] items;
        public AudioClipRefsSO sounds;
        public Material playerRingMaterial;
    }
}
