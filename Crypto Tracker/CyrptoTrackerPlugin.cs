using System.ComponentModel.Composition;
using Widgets.Common;

namespace CryptoTracker
{
    [Export(typeof(IPlugin))]
    internal class CyrptoTrackerPlugin : IPlugin
    {
        public string Name => MainWindow.WidgetName;
        public string? ConfigFile => MainWindow.SettingsFile;
        public WidgetDefaultStruct WidgetDefaultStruct()
        {
            return MainWindow.WidgetDefaultStruct();
        }
        public WidgetWindow WidgetWindow()
        {
            return new MainWindow().WidgetWindow();
        }
    }
}
