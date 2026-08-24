using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Linq; // 為了使用 Contains 語法

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

        // 建構子
        public MainViewModel()
        {
            // 初始化集合
            AvailablePorts = new ObservableCollection<string>();
            RefreshPorts(); // 啟動時先抓一次
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
                AvailablePorts.Add("未偵測到任何 COM Port");
                SelectedPort = AvailablePorts[0];
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
