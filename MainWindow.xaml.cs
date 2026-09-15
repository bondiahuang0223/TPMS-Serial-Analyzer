using Microsoft.Win32; // 負責呼叫 Windows 內建的存檔視窗 (SaveFileDialog)
using System;
using System.IO;       // 負責檔案讀寫 (File.WriteAllText)
using System.IO.Ports; //
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop; // 必須引入這個，用來處理底層 Window Handle (HWND)
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WpfMaterialHello
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // 定義 Windows API 系統常數
        // 0x0219 代表 WM_DEVICECHANGE (裝置變更事件)
        private const int WM_DEVICECHANGE = 0x0219;
        private MainViewModel _viewModel;
        // --- SerialPort 與 Buffer 相關變數 ---
        private SerialPort? _serialPort;
        private StringBuilder _rxBuffer = new StringBuilder();
        private readonly object _bufferLock = new object(); // 用來保護 Buffer 的鎖 (Mutex)
      

        public MainWindow()
        {
            InitializeComponent();
            // 將大腦實例化，並裝進 DataContext 接通綁定通道
            _viewModel = new MainViewModel();
            this.DataContext = _viewModel;
            // --- 訂閱大腦發出的 Command 事件 ---
            _viewModel.OnToggleConnectionRequested += ExecuteToggleConnection;

            // 【新增】：訂閱大腦發出的 Log 顯示事件
 // 【修改】：訂閱大腦發出的 Log 顯示事件，並加入字數上限防護
            _viewModel.OnLogMessageReceived += (message) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    // 設定 UI Log 上限約 50000 個字元 (約幾百行)
                    if (UartLogTextBox.Text.Length > 50000)
                    {
                        // 刪除前半段 (約 5000 字)，並尋找下一個換行符號以保持句子完整
                        int cutIndex = UartLogTextBox.Text.IndexOf('\n', 5000);
                        if (cutIndex >= 0)
                        {
                            UartLogTextBox.Text = UartLogTextBox.Text.Substring(cutIndex + 1);
                        }
                    }

                    UartLogTextBox.AppendText(message);
                    UartLogTextBox.ScrollToEnd();
                });
            };

            // 【新增】：訂閱大腦發出的清空畫面事件
            _viewModel.OnLogCleared += () =>
            {
                lock (_bufferLock)
                {
                    _rxBuffer.Clear();
                }

                Dispatcher.InvokeAsync(() =>
                {
                    UartLogTextBox.Clear();
                });
            };

        }
        // 當視窗載入完成時，會觸發這個方法
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel.RefreshPorts();
        }
        // 當視窗初始化完畢，準備顯示時觸發 (比 Loaded 更早一點)
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // 取得這個 WPF 視窗在作業系統底層的 Handle (控制代碼)
            HwndSource? source = PresentationSource.FromVisual(this) as HwndSource;

            // 將我們自訂的訊息攔截器 (WndProc) 掛載上去
            if (source != null)
            {
                source.AddHook(WndProc);
            }
        }
        private void SafeCloseSerialPort()
        {
            try
            {
                if (_serialPort != null)
                {
                    _serialPort.DataReceived -= SerialPort_DataReceived;
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.InvokeAsync(() => UartLogTextBox.AppendText($"[系統提示] 硬體資源釋放異常: {ex.Message}\n"));
            }
            finally
            {
                if (_serialPort != null)
                {
                    // 針對 Dispose 追加獨立防護
                    try { _serialPort.Dispose(); } catch { }
                    _serialPort = null;
                }
            }
        }
        // 覆寫視窗關閉事件，確保徹底釋放 COM Port
        protected override void OnClosed(EventArgs e)
        {
            SafeCloseSerialPort();
            base.OnClosed(e);
        }

        // 這就是負責接收所有 Windows 系統訊息的攔截器
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 如果收到的訊息是「裝置發生變動」
            if (msg == WM_DEVICECHANGE)
            {
                // 只要裝置有變動（例如插拔 USB、藍牙連線成功建立虛擬 COM），
                // 我們就重新載入下拉選單。
                // 這樣就達到了「完全自動且零負擔」的隨插即用更新！
                //LoadAvailableComPorts();
                _viewModel.RefreshPorts();
            }

            return IntPtr.Zero;
        }       

        // 按鈕點擊事件
        private void ExecuteToggleConnection()
        {
            if (_viewModel.ActionBtnText == "開啟連線")
            {
                string? selectedPort = _viewModel.SelectedPort;
                if (string.IsNullOrWhiteSpace(selectedPort) || selectedPort.Contains("未偵測")) return;

                // 使用區域變數先行測試，成功才接管
                SerialPort tempPort = new SerialPort(selectedPort, _viewModel.SelectedBaudRate, Parity.None, 8, StopBits.One);
                tempPort.DataReceived += SerialPort_DataReceived;
                _serialPort = tempPort;
                try
                {
                    tempPort.Open();
                   
                    

                    _viewModel.ActionBtnText = "關閉連線";
                    PortComboBox.IsEnabled = false;
                    // 若鮑率下拉選單有命名，請一併停用，例如：BaudRateComboBox.IsEnabled = false;
                    if (BaudRateComboBox != null) BaudRateComboBox.IsEnabled = false;
                    UartLogTextBox.AppendText($"--- 已連線至 {selectedPort} ---\n");
                }
                catch (Exception ex)
                {
                    tempPort.DataReceived -= SerialPort_DataReceived;
                    tempPort.Dispose();
                    _serialPort = null;
                    MessageBox.Show($"無法開啟 {selectedPort}:\n{ex.Message}", "連線失敗");
                }
            }
            else
            {
                SafeCloseSerialPort();
                _viewModel.ActionBtnText = "開啟連線";
                PortComboBox.IsEnabled = true;
                if (BaudRateComboBox != null) BaudRateComboBox.IsEnabled = true;
                UartLogTextBox.AppendText($"--- 已中斷連線 ---\n");
            }
        }
        // 相當於 RX Interrupt (注意：這是在背景執行緒執行的！)
 private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            // 驗證 sender，防止快速重連時讀到舊事件的髒資料
            SerialPort? sp = sender as SerialPort;
            if (sp == null || sp != _serialPort || !sp.IsOpen) return;

            try
            {
                string newData = sp.ReadExisting();

                lock (_bufferLock)
                {
                    _rxBuffer.Append(newData);



                    string currentBuffer = _rxBuffer.ToString();
                    int newlineIndex;
                    while ((newlineIndex = currentBuffer.IndexOf('\n')) >= 0)
                    {
                        string line = currentBuffer.Substring(0, newlineIndex).Trim();
                        _rxBuffer.Remove(0, newlineIndex + 1);
                        currentBuffer = _rxBuffer.ToString();

                        if (string.IsNullOrEmpty(line)) continue;
                        
                        string timeStamp = DateTime.Now.ToString("HH:mm:ss.fff");
                        Dispatcher.InvokeAsync(() => _viewModel.ProcessRealTimeData(line, timeStamp));
                    }

                    // 緩衝區溢位防護 (避免無 \n 導致記憶體耗盡)
                    if (_rxBuffer.Length > 4096)
                    {
                        _rxBuffer.Clear();
                        Dispatcher.InvokeAsync(() => _viewModel.ProcessRealTimeData("[系統警告] UART 緩衝區無效累積，已強制清空重置", DateTime.Now.ToString("HH:mm:ss.fff")));
                     
                    }

                }
            }
            catch (Exception ex)
            {
                if (_serialPort == null || !sp.IsOpen)
                {
                    return;
                }
                Dispatcher.InvokeAsync(() => MessageBox.Show($"接收錯誤: {ex.Message}"));
            }
        }

  
        // 將 Hex 字串轉換為 Byte 陣列的工具函式


        // ---------------------------------------------------------
        // 儲存 Log 按鈕事件
        // ---------------------------------------------------------
 


    }
}