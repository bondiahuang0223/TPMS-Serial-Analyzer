using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;


namespace WpfMaterialHello
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // 背景 CSV 緩衝區
        private StringBuilder _csvBuffer = new StringBuilder();
        private bool _isCsvBufferFullWarningSent = false;
        // 宣告讓 UI 訂閱的事件通道
        public event Action<string> OnLogMessageReceived;
        public event Action OnLogCleared;
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
                // 【新增】：強制 WPF 立即重新評估所有 Command 的 CanExecute 狀態
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
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
            // 1. 連線按鈕：必須有選 COM Port，且不能是 "未偵測..."
            ToggleConnectionCommand = new RelayCommand(
                _ => OnToggleConnectionRequested?.Invoke(),
                _ => ActionBtnText == "關閉連線" || (!string.IsNullOrWhiteSpace(SelectedPort) && !SelectedPort.Contains("未偵測"))
            );

            // 2. 清除按鈕：背景 CSV 資料庫長度大於標題列 (約 150 字元) 時才可按
            ClearLogCommand = new RelayCommand(
                _ => ExecuteClearLog()
                
            );

            // 3. 儲存按鈕：條件與清除按鈕相同
            SaveLogCommand = new RelayCommand(
                _ => ExecuteSaveLog(),
                _ => _csvBuffer != null && _csvBuffer.Length > 150
            );

            // 【新增】：初始化 CSV 標題
            ResetCsvBuffer();
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

        private bool _isShowTime = false;
        public bool IsShowTime
        {
            get => _isShowTime;
            set { _isShowTime = value; OnPropertyChanged(); }

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
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        if (Devices.Count >= 20)
                        {
                            Devices.RemoveAt(0);
                        }
                        Devices.Add(device);
                    });
                }

                // 更新時間
                var timeMatch = Regex.Match(line, @"\[(.*?)\]");
                device.Time = timeMatch.Success ? timeMatch.Groups[1].Value : DateTime.Now.ToString("HH:mm:ss");

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


        // =========================================================
        // CSV 寫入與存檔核心邏輯
        // =========================================================
        public void ProcessRealTimeData(string rawLine,string timeStamp)
        {
            string displayLine = IsShowTime ?  $"[{timeStamp}] {rawLine}": rawLine;
            // 1. 拋給 UI TextBox 顯示
            OnLogMessageReceived?.Invoke(displayLine + "\n");


            string taggedLine = $"[{timeStamp}] {rawLine}";
            // 2. 解析並更新卡片數值 (這會呼叫您上面的 ParseUARTString)
            ParseUARTString(taggedLine);

            // 3. 轉換為 CSV 格式並存入背景緩衝區
            AppendToCsvBuffer(taggedLine);
        }

        private void AppendToCsvBuffer(string line)
        {
            int previousLength = _csvBuffer.Length;
            // 【新增】：CSV 容量防護 (設定上限為 5MB)
            if (_csvBuffer.Length > 5 * 1024 * 1024)
            {
                if (!_isCsvBufferFullWarningSent)
                {
                    OnLogMessageReceived?.Invoke("[系統警告] CSV 緩衝區已滿 5MB，已停止紀錄背景資料，請盡快儲存或清除檔案！\n");
                    _isCsvBufferFullWarningSent = true; // 鎖上旗標，避免重複警告
                }
                return;
            }
            if (!line.Contains("Pressure:") && !line.Contains("Revolution:")) return;

            string time = Regex.Match(line, @"\[(.*?)\]").Groups[1].Value;
            string mac = Regex.Match(line, @"([0-9a-fA-F]{2}(?::[0-9a-fA-F]{2}){5})", RegexOptions.IgnoreCase).Value;
            string cnt = Regex.Match(line, @"Cnt:\s*(\d+)").Groups[1].Value;
            string rssi = Regex.Match(line, @"(-\d+)\s*dbm", RegexOptions.IgnoreCase).Groups[1].Value;

            string pressure = Regex.Match(line, @"Pressure:\s*(\d+)").Groups[1].Value;
            string temp = Regex.Match(line, @"Temperature:\s*(-?\d+)").Groups[1].Value;
            string voltage = Regex.Match(line, @"Voltage:\s*(\d+)").Groups[1].Value;
            string mileage = Regex.Match(line, @"Mileage:\s*(\d+)").Groups[1].Value;
            string rev = Regex.Match(line, @"Revolution:\s*(\d+)").Groups[1].Value;
            string foot = Regex.Match(line, @"Footprint:\s*(\d+)").Groups[1].Value;

            _csvBuffer.AppendLine($"{time},{mac},{pressure},{temp},{voltage},{mileage},{rev},{foot},{cnt},{rssi}");
            if (previousLength <= 150 && _csvBuffer.Length > 150)
            {
                App.Current.Dispatcher.InvokeAsync(() =>
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested());
            }
        }

        private void ResetCsvBuffer()
        {
            _csvBuffer.Clear();
            _csvBuffer.AppendLine("Time,MAC,Pressure(kPa),Temp(C),Voltage(mV),Mileage(km),Revolution(us),Footprint(us),Cnt,RSSI(dBm)");
            _isCsvBufferFullWarningSent = false;
        }

        private void ExecuteSaveLog()
        {
            // 只有標題列 (長度大概 100 多) 時不存檔
            if (_csvBuffer.Length <= 150)
            {
                MessageBox.Show("目前沒有任何測試數據可以儲存！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "匯出 CSV 測試數據",
                Filter = "CSV 檔案 (*.csv)|*.csv",
                FileName = $"TPMS_Data_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    // 直接將背景的 StringBuilder 存入硬碟
                    System.IO.File.WriteAllText(saveFileDialog.FileName, _csvBuffer.ToString());
                    MessageBox.Show($"CSV 已成功儲存至：\n{saveFileDialog.FileName}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"儲存檔案時發生錯誤：\n{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExecuteClearLog()
        {
            OnLogCleared?.Invoke(); // 通知畫面清空 TextBox
            ResetCsvBuffer();       // 清空背景 CSV 資料庫並補回標題
        }


        // --- INotifyPropertyChanged 標準實作 ---
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
