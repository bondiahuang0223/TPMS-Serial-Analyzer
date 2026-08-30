using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;


namespace WpfMaterialHello
{
    public class MainViewModel : INotifyPropertyChanged
    {

        // 【關鍵武器 1】：ObservableCollection
        // 它就像是一個會自動廣播的 List。當你對它 Add 或 Clear 時，畫面會自動更新！
        public ObservableCollection<string> AvailablePorts { get; set; }

        // 【關鍵武器 2】：儲存使用者目前選到的 Port
        private string _selectedPort;
        public string SelectedPort
        {
            get { return _selectedPort; }
            set
            {
                _selectedPort = value;
                OnPropertyChanged();
            }
        }


        // 【新增】：Baud Rate 清單與選擇狀態
        public ObservableCollection<int> BaudRates { get; set; } = new ObservableCollection<int>
        {
            9600, 19200, 38400, 57600, 115200, 256000, 460800, 921600
        };

        private int _selectedBaudRate = 256000; // 預設給 TPMS 用的 256000
        public int SelectedBaudRate
        {
            get { return _selectedBaudRate; }
            set { _selectedBaudRate = value; OnPropertyChanged(); }
        }

        // 建構子
        public MainViewModel()
        {
            // 初始化集合
            AvailablePorts = new ObservableCollection<string>();
            RefreshPorts(); // 啟動時先抓一次

            // 將 Command 綁定到對應的事件發送器
            ToggleConnectionCommand = new RelayCommand(_ => OnToggleConnectionRequested?.Invoke());
            ClearLogCommand = new RelayCommand(_ => OnClearLogRequested?.Invoke());
            SaveLogCommand = new RelayCommand(_ => OnSaveLogRequested?.Invoke());
        }

        // 負責更新 COM Port 清單的核心邏輯
        public void RefreshPorts()
        {
            // 先記住目前選的名字，避免刷新後跑掉
            string currentSelection = SelectedPort;

            string[] ports = SerialPort.GetPortNames();

            // 注意：我們在這裡是對「記憶體集合」操作，完全沒有碰到 UI (ComboBox)
            AvailablePorts.Clear();

            if (ports.Length > 0)
            {
                foreach (string port in ports)
                {
                    AvailablePorts.Add(port);
                }

                // 恢復選擇狀態
                if (AvailablePorts.Contains(currentSelection))
                    SelectedPort = currentSelection;
                else
                    SelectedPort = AvailablePorts[0];
            }
            else
            {
                AvailablePorts.Add("未偵測COM Port");
                SelectedPort = AvailablePorts[0];
            }
        }

        // --- MVVM Command 與 UI 狀態綁定 ---
        private string _actionBtnText = "開啟連線";
        public string ActionBtnText
        {
            get => _actionBtnText;
            set { _actionBtnText = value; OnPropertyChanged(); }
        }

        // 定義讓 UI 綁定的按鈕命令
        public ICommand ToggleConnectionCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand SaveLogCommand { get; }

        // 定義對應的事件 (類似硬體中斷旗標)，通知後台執行 UART 動作
        public event Action OnToggleConnectionRequested;
        public event Action OnClearLogRequested;
        public event Action OnSaveLogRequested;

        // 【新增】：用來存放所有解析出來的 TPMS 裝置，UI 會自動把這個清單畫成卡片
        public ObservableCollection<TpmsDevice> Devices { get; set; } = new ObservableCollection<TpmsDevice>();

        // 【新增】：封包解析函式
        public void ParseUARTString(string line)
        {
            // 放寬過濾條件，只要有 Pressure (Format 1) 或 Revolution (Format 2) 就處理
            if (!line.Contains("Pressure:") && !line.Contains("Revolution:")) return;

            try
            {
                var macMatch = Regex.Match(line, @"([0-9a-fA-F]{2}(?::[0-9a-fA-F]{2}){5})", RegexOptions.IgnoreCase);
                if (!macMatch.Success) return;

                string mac = macMatch.Groups[1].Value.ToUpper();
                var device = Devices.FirstOrDefault(d => d.Mac == mac);

                if (device == null)
                {
                    device = new TpmsDevice { Mac = mac };
                    App.Current.Dispatcher.Invoke(() => Devices.Add(device));
                }

                // 更新時間
                device.Time = DateTime.Now.ToString("HH:mm:ss");

                // 解析 Format 1: Pressure, Temp, Voltage, Mileage
                var pMatch = Regex.Match(line, @"Pressure:\s*(\d+)");
                var tMatch = Regex.Match(line, @"Temperature:\s*(-?\d+)");
                var vMatch = Regex.Match(line, @"Voltage:\s*(\d+)");
                var mMatch = Regex.Match(line, @"Mileage:\s*(\d+)");

                if (pMatch.Success) device.Pressure = pMatch.Groups[1].Value;
                if (tMatch.Success) device.Temp = tMatch.Groups[1].Value;
                if (vMatch.Success) device.Voltage = vMatch.Groups[1].Value;
                if (mMatch.Success) device.Mileage = mMatch.Groups[1].Value;

                // 解析 Format 2: Revolution, Footprint
                var revMatch = Regex.Match(line, @"Revolution:\s*(\d+)");
                var footMatch = Regex.Match(line, @"Footprint:\s*(\d+)");

                if (revMatch.Success) device.Revolution = revMatch.Groups[1].Value;
                if (footMatch.Success) device.Footprint = footMatch.Groups[1].Value;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"解析失敗: {ex.Message}");
            }
        }


        // --- INotifyPropertyChanged 標準實作 ---
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
