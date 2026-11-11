using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Modules.Weather.Runtime
{
    // The weather console's presets under the names a mission script uses (IWeatherDirector).
    internal sealed partial class WeatherManager : IWeatherDirector
    {
        public bool Hold(string setPiece) => setPiece switch
        {
            "storm" => ApplyScenario(WeatherScenario.SquallAssault),
            "hurricane" => ApplyScenario(WeatherScenario.HurricaneEye),
            "front" => ApplyScenario(WeatherScenario.FrontalPassage),
            "fog" => ApplyScenario(WeatherScenario.SeaFog),
            "wave" => ApplyScenario(WeatherScenario.MountainWave),
            _ => false,
        };

        public void Resume() => ApplyScenario(WeatherScenario.ResetAll);

        public void OpenConsole()
        {
            if(Presentation.CloudModeCoordinator.Live?.Mode==Presentation.CloudMode.Volumetric)
            {Presentation.MetOfficeMfdLauncher.Live?.OpenWeatherPage();return;}
            if (console == null || !console.IsOpen) ToggleConsole();
        }
    }
}
