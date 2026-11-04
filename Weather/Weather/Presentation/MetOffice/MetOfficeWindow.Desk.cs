using BoscaliSummer.Modules.Weather.Domain;
using NOAvionics;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    // The window's layout: the sky picker (class tabs over the sky gallery) on the left, the sky overhead (with RE-ROLL) and the wind and fronts on the right,
    // and the weather along the wind across the bottom.
    internal sealed partial class MetOfficeWindow
    {
        private readonly AvControl[] classButtons = new AvControl[7];
        private MetSkyGallery gallery;
        private MetCard skyCard, stripCard, timeCard;
        private MetSkyBody sky;
        private AvNote windNote;
        private MetFrontsBody fronts;
        private MetStripBody strip;
        private AvControl simpleNamesButton;
        private static readonly string[] SimpleClasses = { "CLEAR", "FEW", "SOME", "MOSTLY", "CLOUDY", "RAIN", "STORMS" };

        private void BuildPicker()
        {
            AvFlow f = Column("Picker", MetOfficeLayout.Pad, MetOfficeLayout.Left);
            var classSpecs = new AvControl.Spec[7];
            for (int i = 0; i < 7; i++) { var cls = (WeatherRegimeType)i; classSpecs[i] = new AvControl.Spec(MetCatalog.ClassWords[i], () => picker.Browse(cls)); }
            MetControlGrid classGrid = f.Add(new MetControlGrid(f.Content, classSpecs, 7, MetOfficeLayout.Row));
            for (int i = 0; i < 7; i++)
            {
                classButtons[i] = classGrid.Controls[i];
                classButtons[i].SingleLine();
                classButtons[i].Help = MetCatalog.ClassHelp;
            }
            var names = f.Add(new MetControlGrid(f.Content, new[] { new AvControl.Spec("SIMPLE NAMES: ON", ToggleSimpleNames) }, 1, MetOfficeLayout.Row));
            simpleNamesButton = names.Controls[0];
            simpleNamesButton.SingleLine();
            simpleNamesButton.Help = "Switch between everyday descriptions and meteorological preset names. Weather itself is unchanged.";
            gallery = f.Add(new MetSkyGallery(f.Content, s => Press(MetControl.Pin, s), Spec(MetControl.Auto, "AUTO\nMISSION WEATHER", AvIcon.Refresh)));
            Reg(MetControl.Auto, gallery.Auto);
        }

        private void ToggleSimpleNames()
        {
            var entry = BoscaliUnifiedWeather.WeatherPlugin.SimpleNames;
            if (entry != null) entry.Value = !entry.Value;
            nextRefresh = 0f;
            Refresh();
        }

        private void BuildRight()
        {
            AvFlow f = Column("Sky", MetOfficeLayout.Width - MetOfficeLayout.Pad - MetOfficeLayout.Right, MetOfficeLayout.Right);
            skyCard = Card(f, "SKY OVERHEAD");
            Reg(MetControl.Reroll, skyCard.SetHeaderControl(Spec(MetControl.Reroll, "RE-ROLL", AvIcon.Refresh), 96f));
            sky = skyCard.Add(new MetSkyBody(skyCard.Content), MetOfficeLayout.SkyCard - MetOfficeLayout.CardChrome);
            MetCard around = Card(f, "WIND & FRONTS");
            windNote = around.Add(new AvNote(around.Content, AvTextRole.DataStrong, true), MetOfficeLayout.WindLine, 0f);
            fronts = around.Add(new MetFrontsBody(around.Content), MetOfficeLayout.FrontsBody);
        }

        private void BuildStrip()
        {
            stripCard = new MetCard(window.Root, window.Ticker, "ALONG THE WIND");
            window.Ticker.Register(stripCard);
            strip = stripCard.Add(new MetStripBody(stripCard.Content), MetOfficeLayout.StripBody);
            stripCard.Place(new AvSlot(MetOfficeLayout.Pad, MetOfficeLayout.TapeTop, MetOfficeLayout.Width - 2f * MetOfficeLayout.Pad, MetOfficeLayout.TapeCard));
        }

        private void BuildTime()
        {
            timeCard = new MetCard(window.Root, window.Ticker, "TIME OF DAY");
            window.Ticker.Register(timeCard);
            MetControl[] ids = {
                MetControl.TimeMinusHour, MetControl.TimeMinusQuarter,
                MetControl.TimePlusQuarter, MetControl.TimePlusHour,
                MetControl.TimeMidnight, MetControl.TimeMorning,
                MetControl.TimeNoon, MetControl.TimeEvening, MetControl.TimeRestore
            };
            string[] labels = { "-1 HOUR", "-15 MIN", "+15 MIN", "+1 HOUR", "00:00", "06:00", "12:00", "18:00", "RESTORE TIME" };
            var specs = new AvControl.Spec[ids.Length];
            for (int i = 0; i < ids.Length; i++) specs[i] = Spec(ids[i], labels[i]);
            var grid = timeCard.Add(new MetControlGrid(timeCard.Content, specs, ids.Length, MetOfficeLayout.Row), MetOfficeLayout.Row);
            for (int i = 0; i < ids.Length; i++) { Reg(ids[i], grid.Controls[i]); grid.Controls[i].SingleLine(); }
            timeCard.Place(new AvSlot(MetOfficeLayout.Pad, MetOfficeLayout.TimeTop, MetOfficeLayout.Width - 2f * MetOfficeLayout.Pad, MetOfficeLayout.TimeCard));
        }

        private void RefreshDesk()
        {
            bool simple = BoscaliUnifiedWeather.WeatherPlugin.SimpleNames?.Value ?? true;
            for (int i = 0; i < classButtons.Length; i++) { classButtons[i].Label = simple ? SimpleClasses[i] : MetCatalog.ClassWords[i]; classButtons[i].Latched = (int)picker.Class == i; }
            MetState pin = MetOfficeRules.State(MetControl.Pin, model);
            simpleNamesButton.Label = simple ? "SIMPLE NAMES: ON" : "SIMPLE NAMES: OFF";
            simpleNamesButton.Latched = simple;
            gallery.Show(picker.Class, model.PinnedSky, model.Captured ? model.SkyNumber - 1 : -1, pin.Enabled, MetOfficeRules.Tip(MetControl.Pin, model), simple);
            control[(int)MetControl.Auto].Latched = model.Captured && model.PinnedSky < 0 && !model.Forced;
            sky.Set(model, simple);
            skyCard.SetRail(sky.RailState);
            windNote.Set(model.Captured ? MetOfficeText.WindStrip(model.WindFromDeg, model.WindMps) : "");
            fronts.Set(model);
            strip.Set(model);
            stripCard.SetCaption(model.SkyRole.Length > 0 ? "OVERHEAD · " + model.SkyRole : "");
            timeCard.SetCaption(model.Captured ? TimeOfDayMath.ClockText(model.TimeOfDayHours) + (model.TimeChanged ? " · ADJUSTED" : " · MISSION CLOCK") : "WAITING FOR MISSION");
        }
    }
}
