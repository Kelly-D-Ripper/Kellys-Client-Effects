namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>What the host can do in the weather console: set a sky (a tile, or the previous / next sky of the class on show), hand the weather back to the mission, re-roll the layout.</summary>
    internal enum MetControl : byte
    {
        Pin, SkyPrev, SkyNext, Auto, Reroll,
        TimeMinusHour, TimeMinusQuarter, TimePlusQuarter, TimePlusHour,
        TimeMidnight, TimeMorning, TimeNoon, TimeEvening, TimeRestore
    }

    /// <summary>The fixed words of the window: control names and help sentences (one footer line each), class codes and the read-out tips.</summary>
    internal static class MetCatalog
    {
        public const string DtgHelp = "DTG is the real UTC clock (cosmetic). T+ is the mission clock.";
        public const string RoleHostHelp = "SINGLE PLAYER: weather and time controls affect this solo mission only.";
        public const string RoleClientHelp = "READ ONLY: weather and time changes are available in single-player missions only.";
        public const string ClassHelp = "CLASS: shows the skies of this class. Browsing changes nothing; click a sky to set it.";

        /// <summary>Class codes and words, in <see cref="WeatherRegimeType"/> order.</summary>
        public static readonly string[] ClassCodes = { "CLR", "FEW", "SCT", "BKN", "OVC", "RA+", "TS" };
        public static readonly string[] ClassWords = { "CLEAR", "FAIR", "SCATTERED", "BROKEN", "OVERCAST", "RAIN", "STORM" };

        public static string Name(MetControl id) => id switch
        {
            MetControl.Pin => "SKY",
            MetControl.SkyPrev => "PREVIOUS SKY",
            MetControl.SkyNext => "NEXT SKY",
            MetControl.Auto => "AUTO",
            MetControl.TimeMinusHour => "TIME -1 HOUR",
            MetControl.TimeMinusQuarter => "TIME -15 MIN",
            MetControl.TimePlusQuarter => "TIME +15 MIN",
            MetControl.TimePlusHour => "TIME +1 HOUR",
            MetControl.TimeMidnight => "MIDNIGHT",
            MetControl.TimeMorning => "MORNING",
            MetControl.TimeNoon => "NOON",
            MetControl.TimeEvening => "EVENING",
            MetControl.TimeRestore => "RESTORE TIME",
            _ => "RE-ROLL",
        };

        public static string Help(MetControl id) => id switch
        {
            MetControl.Pin => "SKY: set the weather for this solo mission. Extreme storm uses Rain haze for fog and Rain sound for thunder. AUTO restores mission weather.",
            MetControl.SkyPrev => "PREVIOUS SKY of the class on show, set at once (key: left arrow).",
            MetControl.SkyNext => "NEXT SKY of the class on show, set at once (key: right arrow).",
            MetControl.Auto => "AUTO: the mission's own weather again. Clears a set sky and anything a mission script forced (key: backspace).",
            MetControl.TimeMinusHour => "TIME: move the sun clock back one hour. Mission elapsed time and weather progress are unchanged.",
            MetControl.TimeMinusQuarter => "TIME: move the sun clock back 15 minutes. Mission elapsed time and weather progress are unchanged.",
            MetControl.TimePlusQuarter => "TIME: move the sun clock forward 15 minutes. Mission elapsed time and weather progress are unchanged.",
            MetControl.TimePlusHour => "TIME: move the sun clock forward one hour. Mission elapsed time and weather progress are unchanged.",
            MetControl.TimeMidnight => "MIDNIGHT: set the sun clock to 00:00 in this mission. Time continues at its existing rate.",
            MetControl.TimeMorning => "MORNING: set the sun clock to 06:00 in this mission. Time continues at its existing rate.",
            MetControl.TimeNoon => "NOON: set the sun clock to 12:00 in this mission. Time continues at its existing rate.",
            MetControl.TimeEvening => "EVENING: set the sun clock to 18:00 in this mission. Time continues at its existing rate.",
            MetControl.TimeRestore => "RESTORE TIME: remove your clock adjustment while keeping the time that has naturally passed. Does not change the weather.",
            _ => "RE-ROLL: same weather, new places for the clouds and storms (key: R).",
        };
    }
}
