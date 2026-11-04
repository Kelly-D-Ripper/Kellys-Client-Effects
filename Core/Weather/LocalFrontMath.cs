using System;

namespace BoscaliClientExperimental.Weather
{
    // One cosmetic moving rain band. Derived from Boscali FrontLine's cold-front
    // Gaussian (18 mm/h, sigma 2400 m) and SkyMath's 0.3-0.6 cover gate.
    // Host conditions are only a rough visual gate, not observed precipitation.
    internal static class LocalFrontMath
    {
        private const double Sigma = 2400.0, Period = 40000.0;
        internal static float Intensity(double alongMetres, double elapsed, float speed,
            float conditions, float altitude, float cloudBase)
        {
            if (!Finite(alongMetres) || !Finite(elapsed) || !Finite(speed) ||
                !Finite(conditions) || !Finite(altitude) || !Finite(cloudBase) || cloudBase <= 0f)
                return 0f;
            double safeSpeed = Math.Max(8.0, Math.Min(30.0, speed));
            double offset = alongMetres - safeSpeed * Math.Max(0.0, elapsed) + 2000.0;
            double distance = offset - Math.Floor((offset + Period / 2.0) / Period) * Period;
            double ratio = distance / Sigma;
            double gaussian = Math.Exp(-ratio * ratio);
            double cover = Smooth(0.3, 0.6, conditions);
            double belowCloud = 1.0 - Smooth(cloudBase - 300.0, cloudBase + 200.0, altitude);
            return (float)Math.Max(0.0, Math.Min(1.0, 0.9 * gaussian * cover * belowCloud));
        }
        private static double Smooth(double a, double b, double x)
        {
            double t = Math.Max(0.0, Math.Min(1.0, (x - a) / (b - a)));
            return t * t * (3.0 - 2.0 * t);
        }
        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }
}
