using BoscaliSummer.Modules.Weather.Runtime;
using UnityEngine;
namespace BoscaliSummer.Modules.Weather.Networking {
    // Intentionally no network registration, inbound handlers, or sends. Multiplayer is disabled.
    internal sealed class WeatherNet : MonoBehaviour {
        public const byte ProtocolVersion = 8;
        public void Configure(WeatherManager manager) { }
        public void ResetScene() { }
        public void ArmResends() { }
        public void Broadcast(WeatherSyncMessage message) { }
    }
}
