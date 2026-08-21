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

namespace WpfMaterialHello
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        // 按鈕點擊事件
        private void ActionBtn_Click(object sender, RoutedEventArgs e)
        {
            // 讀取 TextBox 的內容
            string inputText = InputTextBox.Text;

            // 讀取 ComboBox 目前顯示的文字
            string selectedPort = PortComboBox.Text;

            // 檢查是否有選擇 COM Port
            if (string.IsNullOrWhiteSpace(selectedPort))
            {
                MessageBox.Show("請先選擇一個 COM Port！", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(inputText))
            {
                MessageBox.Show("請輸入測試指令！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            bool isHexMode = (HexModeToggle.IsChecked == true);

            if (isHexMode)
            {
                // 如果開關是打開的，假設我們做 Hex 處理
                ActionBtn.Content = $"已用 [Hex模式]向 {selectedPort} 送出";
            }
            else
            {
                // 如果開關是關閉的，一般字串處理
                ActionBtn.Content = $"已用 [字串模式] 送出";
            }
            // 模擬連線與發送指令的結果
            //ActionBtn.Content = $"已向 {selectedPort} 送出指令";

            // 清空輸入框，但保留 COM Port 的選擇
            InputTextBox.Text = string.Empty;
        }
  

    }
}