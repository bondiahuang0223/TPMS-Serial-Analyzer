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
using System.IO;       // 負責檔案讀寫 (File.WriteAllText)
using Microsoft.Win32; // 負責呼叫 Windows 內建的存檔視窗 (SaveFileDialog)
using System;

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
            if (_serialPort == null || !_serialPort.IsOpen)
            {
                // 【執行連線】
                string selectedPort = _viewModel.SelectedPort;
                if (string.IsNullOrWhiteSpace(selectedPort) || selectedPort.Contains("未偵測"))
                {
                    MessageBox.Show("請先選擇一個有效的 COM Port！", "錯誤");
                    return;
                }

                try
                {
                    // 【修正】：將 115200 替換為 _viewModel.SelectedBaudRate
                    _serialPort = new SerialPort(selectedPort, _viewModel.SelectedBaudRate, Parity.None, 8, StopBits.One);

                    _serialPort.DataReceived += SerialPort_DataReceived;
                    _serialPort.Open();
                    _uiUpdateTimer.Start();

                    // 修改按鈕內的文字 (不影響 Icon)
                    ActionBtnText.Text = "關閉連線";
                    PortComboBox.IsEnabled = false;
                    UartLogTextBox.AppendText($"--- 已連線至 {selectedPort} ---\n");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"無法開啟 {selectedPort}:\n{ex.Message}", "連線失敗");
                }
            }
            else
            {
                // 【執行斷線】
                _uiUpdateTimer.Stop();
                _serialPort.DataReceived -= SerialPort_DataReceived;
                _serialPort.Close();

                // 恢復按鈕文字
                ActionBtnText.Text = "開啟連線";
                PortComboBox.IsEnabled = true;
                UartLogTextBox.AppendText($"--- 已中斷連線 ---\n");
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
                // 【優化】：將字串依換行符號切開，逐行交給大腦解析，確保不漏接任何一顆輪胎的封包
                string[] lines = dataToDisplay.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                // 判斷是否開啟「顯示時間」
                bool isShowTime = (ShowTimeToggle.IsChecked == true);

                // 取得當下的時間
                string timeStamp = DateTime.Now.ToString("HH:mm:ss.ff");


                foreach (string line in lines)
                {
                    _viewModel.ParseUARTString(line);

                    if (isShowTime)
                    {
                        UartLogTextBox.AppendText($"[{timeStamp}] {line}\n");
                    }
                    else
                    {
                        UartLogTextBox.AppendText($"{line}\n");
                    }
                }



                UartLogTextBox.ScrollToEnd();
            }
        }
        // 將 Hex 字串轉換為 Byte 陣列的工具函式
  

        // ---------------------------------------------------------
        // 儲存 Log 按鈕事件
        // ---------------------------------------------------------
        private void SaveLogBtn_Click(object sender, RoutedEventArgs e)
        {
            // 1. 檢查是否有內容可以存檔
            if (string.IsNullOrEmpty(UartLogTextBox.Text))
            {
                MessageBox.Show("目前沒有任何 Log 可以儲存！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 2. 建立並設定存檔對話框
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = "儲存 UART 接收紀錄";
            saveFileDialog.Filter = "文字檔案 (*.txt)|*.txt|所有檔案 (*.*)|*.*"; // 限定存檔類型
            
            // 預設檔名帶上當下時間，方便工程師管理檔案 (例如: UART_Log_20260824_173000.txt)
            saveFileDialog.FileName = $"UART_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"; 

            // 3. 顯示對話框，如果使用者按下了「存檔」
            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    // 使用一行程式碼，將 TextBox 內的所有文字寫入使用者指定的檔案路徑
                    File.WriteAllText(saveFileDialog.FileName, UartLogTextBox.Text);
                    
                    MessageBox.Show($"Log 已成功儲存至：\n{saveFileDialog.FileName}", "儲存成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"儲存檔案時發生錯誤：\n{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        // ---------------------------------------------------------
        // 清除 Log 按鈕事件
        // ---------------------------------------------------------
        private void ClearLogBtn_Click(object sender, RoutedEventArgs e)
        {
            // 清空畫面上的文字
            UartLogTextBox.Clear();

            // 為了確保 Buffer 的資料也乾淨，建議一併清空我們在背景使用的 StringBuilder
            lock (_bufferLock)
            {
                _rxBuffer.Clear();
            }
        }


    }
}