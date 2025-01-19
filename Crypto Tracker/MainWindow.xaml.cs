using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Widgets.Common;

namespace CryptoTracker
{
    public partial class MainWindow : Window,IWidgetWindow
    {
        public readonly static string WidgetName = "Crypto Tracker";
        public readonly static string SettingsFile = "settings.cryptotracker.json";

        private readonly Config config = new(SettingsFile);
        public CryptoTrackerViewModel CryptoTrackerViewModel;
        private CryptoTrackerViewModel.SettingsStruct Settings = CryptoTrackerViewModel.Default;

        private readonly List<CoinPair> CoinPairs = [];
        public ObservableCollection<CoinPair> CoinPairsFiltered;
        public ObservableCollection<CoinPair> CoinPairGridRows;

        private readonly Request request = new();
        private readonly Schedule schedule = new();
        private readonly List<string> scheduleIds = [];
        public static bool WindowClosed = false;

        public MainWindow()
        {
            LoadSettings();
            CryptoTrackerViewModel = new()
            {
                Settings = Settings
            };
            CoinPairsFiltered = CryptoTrackerViewModel.CoinPairsFiltered;
            CoinPairGridRows = CryptoTrackerViewModel.CoinPairGridRows;

            Logger.Info($"{WidgetName} is started");
            foreach (CoinPair pair in CoinPairGridRows)
            {
                Logger.Info("Symbol: " + pair.Symbol);
            }

            InitializeComponent();
            DataContext = CryptoTrackerViewModel;
            Loaded += MainWindow_Loaded;
        }

        public WidgetWindow WidgetWindow()
        {
            return new WidgetWindow(this);
        }

        public static WidgetDefaultStruct WidgetDefaultStruct()
        {
            return new()
            {
                Height = 600,
                Width = 400,
                SizeToContent = SizeToContent.Height
            };
        }

        public void LoadSettings()
        {
            try
            {
                string selected_pairs = PropertyParser.ToString(config.GetValue("CoinPairGridRows"));
                Settings.CoinPairGridRows = JsonConvert.DeserializeObject<ObservableCollection<CoinPair>>(selected_pairs) ?? [];

                Settings.FontSize = PropertyParser.ToFloat(config.GetValue("fontsize"));
                Settings.FontColor = PropertyParser.ToColorBrush(config.GetValue("fontcolor"), Settings.FontColor.ToString());
                Settings.HeaderFontColor = PropertyParser.ToColorBrush(config.GetValue("header_fontcolor"), Settings.HeaderFontColor.ToString());
                Settings.Background = PropertyParser.ToColorBrush(config.GetValue("background"), Settings.Background.ToString());
                Settings.DividerColor = PropertyParser.ToColorBrush(config.GetValue("divider_color"), Settings.DividerColor.ToString());
            }
            catch (Exception ex)
            {
                config.Add("fontsize", Settings.FontSize);
                config.Add("fontcolor", Settings.FontColor);
                config.Add("header_fontcolor", Settings.HeaderFontColor);
                config.Add("background", Settings.Background);
                config.Add("divider_color", Settings.DividerColor);
                config.Add("CoinPairGridRows", Settings.CoinPairGridRows);
                config.Save();
                Logger.Info(ex.Message);
            }
        }


        // Add all Symbol from settings
        private async Task AddSymbols(ObservableCollection<CoinPair> coinPairGridRows)
        {
            await request.GetOpenPrices(coinPairGridRows);
            foreach (var pair in coinPairGridRows.ToList())
            {
                _ = request.GetCoinImage(pair);
            }
            await request.WebSocketConnection(coinPairGridRows);
        }

        // Add new Symbol to the DataGrid row
        private async void AddSymbol(CoinPair selectedPair)
        {
            CoinPairGridRows.Add(selectedPair);
            await request.GetOpenPrice(selectedPair);
            _ = request.GetCoinImage(selectedPair);
            request.Subscribe(selectedPair);

            config.Add("CoinPairGridRows", CoinPairGridRows);
            config.Save();
        }

        // Datagrid delete row object
        private void RemoveSymbol(CoinPair selectedPair)
        {
            request.UnSubscribe(selectedPair);
            CoinPairGridRows.Remove(selectedPair);
            config.Add("CoinPairGridRows", CoinPairGridRows);
            config.Save();
        }


        #region Events Handled
        // WebSocketConnection on window loaded
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            WindowClosed = false;
            DateTime utcNow = DateTime.UtcNow;
            DateTime nextDayUtc = utcNow.AddDays(1).Date.AddSeconds(5);

            scheduleIds.Add(schedule.Daily(async () => await request.LoadCoinPairsAsync(CoinPairs), 1));
            scheduleIds.Add(schedule.Daily(async () => await request.GetOpenPrices(CoinPairGridRows), 1, nextDayUtc));

            await request.LoadCoinPairsAsync(CoinPairs);
            await AddSymbols(CoinPairGridRows);
        }

        //When window closing
        protected override async void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            WindowClosed = true;

            foreach (var timerId in scheduleIds)
            {
                schedule.Stop(timerId);
            }
            scheduleIds.Clear();
            await request.CloseWebSockets();
            request.Dispose();
            Logger.Info($"{WidgetName} is closed");
        }

        // DataGrid delete selected row
        public void DeleteDataGridRow(object sender, RoutedEventArgs e)
        {
            Label? clickedButton = sender as Label;
            DependencyObject? parent = clickedButton?.Parent;

            while (parent != null && parent is not DataGridRow)
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent is DataGridRow dataGridRow)
            {
                if (dataGridRow.Item is CoinPair deletedRow)
                {
                    RemoveSymbol(deletedRow);
                }
            }
        }

        // Search Crypto Symbol
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = SearchBox.Text.ToUpper();

            Application.Current.Dispatcher.Invoke(() =>
            {
                CoinPairsFiltered.Clear();
                if (query == "")
                {
                    PlaceHolder.Opacity = 0.5;
                    SearchPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                PlaceHolder.Opacity = 0;
                SearchPanel.Visibility = Visibility.Visible;

                foreach (var coinPair in CoinPairs)
                {
                    if (coinPair.Symbol.StartsWith(query))
                        CoinPairsFiltered.Add(coinPair);
                    if (CoinPairsFiltered.Count >= 10)
                        break;
                }
            });
        }

        // switch focus from searchbox to searchresults
        private void SearchBox_DownArrow(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && SearchResults.Items.Count > 0)
            {
                SearchResults.SelectedIndex = 0;
                SearchResults.Focus();
            }
        }

        // Select searchresult with mouse
        private void SearchResults_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (SearchResults.SelectedItem != null)
            {
                if (SearchResults.SelectedItem is CoinPair selectedPair)
                {
                    AddSymbol(selectedPair);
                    SearchPanel.Visibility = Visibility.Collapsed;
                    SearchBox.Focus();
                    SearchBox.Text = "";
                }
            }
        }

        // Select searchresult with Enter
        private void SearchResults_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && SearchResults.SelectedItem != null)
            {
                if (SearchResults.SelectedItem is CoinPair selectedPair)
                {
                    AddSymbol(selectedPair);
                    SearchPanel.Visibility = Visibility.Collapsed;
                    SearchBox.Focus();
                    SearchBox.Text = "";
                }
            }
        }

        // DataGridRow Hover start event
        private void DataGridRow_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is DataGridRow row)
            {
                row.Background = Settings.DividerColor;
                Label? targetButton = FindChildButtonWithTag(row, "DeleteButton");

                if (row.DataContext is CoinPair && targetButton != null)
                {
                    targetButton.Visibility = Visibility.Visible;
                }
            }
        }

        // DataGridRow Hover end event
        private void DataGridRow_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is DataGridRow row)
            {
                row.Background = Brushes.Transparent;
                Label? targetButton = FindChildButtonWithTag(row, "DeleteButton");

                if (targetButton != null)
                {
                    targetButton.Visibility = Visibility.Collapsed;
                }
            }
        }

        // DataGridRow restore sorting to original
        private void DataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            if (e.Column.SortMemberPath == "ID")
            {
                e.Handled = true;
                var view = CollectionViewSource.GetDefaultView(CoinPairGridRows);
                view.SortDescriptions.Add(new SortDescription("ID", ListSortDirection.Ascending));
                view.SortDescriptions.Clear();
                view.Refresh();
            }
            else
            {
                e.Handled = false;
            }
        }

        // DataGrid save when order changes with drag drop
        private void DataGrid_Drop(object sender, DragEventArgs e)
        {
            if (sender is DataGrid dataGrid && dataGrid.ItemsSource is ObservableCollection<CoinPair>)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    config.Add("CoinPairGridRows", CoinPairGridRows);
                    config.Save();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        // DataGrid DeleteButton finder
        private static Label? FindChildButtonWithTag(DependencyObject parent, string tag)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is Label button && button.Tag?.ToString() == tag)
                {
                    return button;
                }

                // recursive
                var result = FindChildButtonWithTag(child, tag);
                if (result != null)
                    return result;
            }
            return null;
        }
        #endregion Events Handled End
    }
}