using Newtonsoft.Json;
using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace CryptoTracker
{
    public class CoinPair : INotifyPropertyChanged
    {
        public string? BaseAsset { get; set; }      // BTC
        public string? QuoteAsset { get; set; }     // USDT

        private BitmapImage? _image;
        [JsonIgnore]
        public BitmapImage? Image
        {
            get => _image;
            set
            {
                if (_image != value)
                {
                    _image = value;
                    OnPropertyChanged(nameof(Image));
                }
            }
        }


        private string _symbol = "";
        required public string Symbol
        {
            get => _symbol;
            set
            {
                if (_symbol != value)
                {
                    _symbol = value;
                    OnPropertyChanged(nameof(Symbol));
                }
            }
        }

        private double _price;
        public double Price
        {
            get => _price;
            set
            {
                if (_price != value)
                {
                    _price = value;
                    OnPropertyChanged(nameof(Price));
                    OnPropertyChanged(nameof(PriceString));
                }
            }
        }

        private double _change;
        public double Change
        {
            get => _change;
            set
            {
                if (_change != value)
                {
                    _change = value;
                    OnPropertyChanged(nameof(Change));
                    OnPropertyChanged(nameof(ChangeString));
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        private double _changePercent;
        public double ChangePercent
        {
            get => _changePercent;
            set
            {
                if (_changePercent != value)
                {
                    _changePercent = value;
                    OnPropertyChanged(nameof(ChangePercent));
                    OnPropertyChanged(nameof(DisplayPercentage));
                }
            }
        }


        public string PriceString => NumberFormat(Price);
        public string ChangeString => Price > 1 ? Change.ToString("F2") : NumberFormat(Change);
        public string DisplayPercentage => ChangePercent.ToString("F2") + "%";
        public int Status => Change > 0 ? 1 : (Change < 0 ? -1 : 0);

        private static string NumberFormat(double number, int _decimal = 2)
        {
            if (Math.Abs(number) < 1)
            {
                _decimal = Math.Round(1 / Math.Abs(number)).ToString().Replace(".", "").Length + 1;
            }

            return number.ToString($"F{_decimal}");
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
