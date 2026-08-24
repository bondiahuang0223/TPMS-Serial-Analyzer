using System.IO.Ports; //
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Interop; // 必須引入這個，用來處理底層 Window Handle (HWND)
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
        private SerialPort _serialPort;
        private StringBuilder _rxBuffer = new StringBuilder();
        private readonly object _bufferLock = new object(); // 用來保護 Buffer 的鎖 (Mutex)
        private DispatcherTimer _uiUpdateTimer;

        public MainWindow()
        {
            InitializeComponent();
            // 將大腦實例化，並裝進 DataContext 接通綁定通道
            _viewModel = new MainViewModel();
            this.DataContext = _viewModel;
            // 初始化 UI 更新定時器 (設定為每 500 毫秒觸發一次)
            _uiUpdateTimer = new DispatcherTimer();
            _uiUpdateTimer.Interval = TimeSpan.FromMilliseconds(500);
            _uiUpdateTimer.Tick += UiUpdateTimer_Tick;
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
            HwndSource source = PresentationSource.FromVisual(this) as HwndSource;

            // 將我們自訂的訊息攔截器 (WndProc) 掛載上去
            if (source != null)
            {
                source.AddHook(WndProc);
            }
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
        }        // 獨立出一個方法來載入 COM Port，方便以後按下「重新整理」按鈕時也可以呼叫

        // 按鈕點擊事件
        private void ActionBtn_Click(object sender, RoutedEventArgs e)
        {
            // 如果 SerialPort 尚未建立或尚未開啟，則執行【連線】邏輯
            if (_serialPort == null || !_serialPort.IsOpen)
            {
                string selectedPort = _viewModel.SelectedPort;
                if (string.IsNullOrWhiteSpace(selectedPort) || selectedPort.Contains("未偵測"))
                {
                    MessageBox.Show("請先選擇一個有效的 COM Port！", "錯誤");
                    return;
                }

                try
                {
                    // 實例化 SerialPort (可以依需求修改 BaudRate)
                    _serialPort = new SerialPort(selectedPort, 115200, Parity.None, 8, StopBits.One);
                    _serialPort.DataReceived += SerialPort_DataReceived;

                    _serialPort.Open();
                    _uiUpdateTimer.Start(); // 開始定時更新 UI

                    // 更新 UI 狀態
                    ActionBtn.Content = "關閉連線";
                    PortComboBox.IsEnabled = false; // 連線中不允許更改 Port
                    UartLogTextBox.AppendText($"--- 已連線至 {selectedPort} ---\n");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"無法開啟 {selectedPort}:\n{ex.Message}", "連線失敗");
                }
            }
            else
            {
                // 如果已經連線，則執行【發送指令】或【斷線】邏輯
                string inputText = InputTextBox.Text;

                if (!string.IsNullOrWhiteSpace(inputText))
                {
                    bool isHexMode = (HexModeToggle.IsChecked == true);

                    // 產生毫秒等級的時間戳記
                    string timeStamp = DateTime.Now.ToString("HH:mm:ss.fff");

                    if (isHexMode)
                    {
                        try
                        {
                            // 1. 呼叫剛剛寫的副程式進行轉換
                            byte[] hexBytes = ConvertHexStringToByteArray(inputText);

                            // 2. 透過 SerialPort 送出 Byte 陣列
                            _serialPort.Write(hexBytes, 0, hexBytes.Length);

                            // 3. UI 顯示 (利用 BitConverter 將陣列轉回漂亮的帶 '-' 字串，並替換成空白)
                            string displayHex = BitConverter.ToString(hexBytes).Replace("-", " ");
                            UartLogTextBox.AppendText($"[{timeStamp} TX Hex]: {displayHex}\n");
                            UartLogTextBox.ScrollToEnd();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Hex 格式轉換失敗，請確認輸入是否正確 (0-9, A-F)。\n錯誤: {ex.Message}", "格式錯誤");
                            return; // 轉換失敗就不清除輸入框
                        }
                    }
                    else
                    {
                        // 一般字串發送 (自動補上換行符號)
                        _serialPort.WriteLine(inputText);
                        UartLogTextBox.AppendText($"[{timeStamp} TX]: {inputText}\n");
                        UartLogTextBox.ScrollToEnd();
                    }

                    InputTextBox.Text = string.Empty;
                }
                else
                {
                    // 斷線邏輯保持不變...
                    _uiUpdateTimer.Stop();
                    _serialPort.DataReceived -= SerialPort_DataReceived;
                    _serialPort.Close();

                    ActionBtn.Content = "連線並發送";
                    PortComboBox.IsEnabled = true;
                    UartLogTextBox.AppendText($"--- 已中斷連線 ---\n");
                }
            }
        }
        // 相當於 RX Interrupt (注意：這是在背景執行緒執行的！)
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                // 一次把目前硬體 FIFO 裡的字串全讀出來
                string newData = _serialPort.ReadExisting();

                // 進入臨界區 (Critical Section)，鎖住 Buffer 寫入資料
                lock (_bufferLock)
                {
                    _rxBuffer.Append(newData);
                }
            }
            catch (Exception ex)
            {
                // 處理可能發生的通訊例外 (例如裝置突然拔除)
                Dispatcher.Invoke(() => MessageBox.Show($"接收錯誤: {ex.Message}"));
            }
        }

        // 相當於 Main Loop 定時檢查 Buffer (注意：這是在 UI 執行緒執行的！)
        private void UiUpdateTimer_Tick(object sender, EventArgs e)
        {
            string dataToDisplay = null;

            // 進入臨界區，檢查並抽許資料
            lock (_bufferLock)
            {
                if (_rxBuffer.Length > 0)
                {
                    string currentBuffer = _rxBuffer.ToString();

                    // 尋找最後一個換行符號的位置
                    int lastNewlineIndex = currentBuffer.LastIndexOf('\n');

                    if (lastNewlineIndex >= 0)
                    {
                        // 擷取到最後一個 \n 的部分 (包含 \n 本身)
                        int extractLength = lastNewlineIndex + 1;
                        dataToDisplay = currentBuffer.Substring(0, extractLength);

                        // 將已經擷取出來的部分，從 Buffer 中刪除 (類似清除已讀取的 Ring Buffer)
                        _rxBuffer.Remove(0, extractLength);
                    }
                }
            }

            // 如果有抽取出完整的字串，就更新到 UI 的 TextBox 上
            // 如果有抽取出完整的字串，就更新到 UI 的 TextBox 上
            if (!string.IsNullOrEmpty(dataToDisplay))
            {
                // 取得當前時間
                string timeStamp = DateTime.Now.ToString("HH:mm:ss.fff");

                // 在輸出的最前面加上 [時間 RX]:
                UartLogTextBox.AppendText($"[{timeStamp} RX]: {dataToDisplay}");
                UartLogTextBox.ScrollToEnd();
            }
        }
        // 將 Hex 字串轉換為 Byte 陣列的工具函式
        private byte[] ConvertHexStringToByteArray(string hexString)
        {
            // 1. 移除所有空白，方便使用者貼上 "01 0A 0B" 這種格式
            hexString = hexString.Replace(" ", "");

            // 2. 防呆：如果長度是奇數，在前面補 0 (例如輸入 "A" 變成 "0A")
            if (hexString.Length % 2 != 0)
            {
                hexString = "0" + hexString;
            }

            // 3. 每兩個字元切一刀，轉換成 byte
            byte[] bytes = new byte[hexString.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                // Convert.ToByte(字串, 16進位)
                bytes[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
            }

            return bytes;
        }

    }
}