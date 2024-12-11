using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;

namespace CryptoTracker
{
    public partial class CryptoTrackerViewModel
    {
        public struct SettingsStruct
        {
            public ObservableCollection<CoinPair> CoinPairGridRows { get; set; }
            public float FontSize { get; set; }
            public SolidColorBrush FontColor { get; set; }
            public SolidColorBrush HeaderFontColor { get; set; }
            public SolidColorBrush Background { get; set; }
            public SolidColorBrush DividerColor { get; set; }
        }

        public static SettingsStruct Default => new()
        {
            CoinPairGridRows = [],
            FontSize = 14,
            FontColor = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            HeaderFontColor = new SolidColorBrush(Colors.White),
            Background = new SolidColorBrush(Colors.Black),
            DividerColor = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
        };

        public ObservableCollection<CoinPair> CoinPairsFiltered { get; set; } = [];
        private ObservableCollection<CoinPair> _coinPairGridRows = [];
        public ObservableCollection<CoinPair> CoinPairGridRows
        {
            get { return _coinPairGridRows; }
            set { _coinPairGridRows = value; }
        }

        private SettingsStruct _settings;
        public required SettingsStruct Settings
        {
            get { return _settings; }
            set { 
                _settings = value;
                CoinPairGridRows = _settings.CoinPairGridRows;
                OnPropertyChanged(nameof(Settings));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
