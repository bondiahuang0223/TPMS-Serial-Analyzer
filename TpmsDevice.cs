using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfMaterialHello
{
    public class TpmsDevice : INotifyPropertyChanged
    {
        private string _pressure;
        private string _temp;
        private string _voltage;
        private string _mileage;
        private string _revolution;
        private string _footprint;
        private string _time;

        public string Mac { get; set; }

        public string Pressure { get => _pressure; set { _pressure = value; OnPropertyChanged(); UpdateLoadEstimation(); } }
        public string Temp { get => _temp; set { _temp = value; OnPropertyChanged(); } }

        // 電壓改為只存數值供背景判斷，不再直接綁定到 UI 顯示大字
        public string Voltage { get => _voltage; set { _voltage = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsLowVoltage)); } }

        public string Mileage { get => _mileage; set { _mileage = value; OnPropertyChanged(); } }
        public string Revolution { get => _revolution; set { _revolution = value; OnPropertyChanged(); UpdateLoadEstimation(); } }
        public string Footprint { get => _footprint; set { _footprint = value; OnPropertyChanged(); UpdateLoadEstimation(); } }
        public string Time { get => _time; set { _time = value; OnPropertyChanged(); } }

        // 低電壓告警：協定指出 Format 1 的 Status Bit[0]=1 代表低於 2.3V，
        // 這裡我們直接用數值判斷 (低於 2300mV 觸發)
        public bool IsLowVoltage => int.TryParse(Voltage, out int v) && v < 2300;

        // 計算出來的荷重結果
        private string _estimatedLoad = "--";
        public string EstimatedLoad { get => _estimatedLoad; set { _estimatedLoad = value; OnPropertyChanged(); } }

        // --- 荷重估算演算法 ---
        private void UpdateLoadEstimation()
        {
            if (double.TryParse(Pressure, out double p) &&
                double.TryParse(Revolution, out double rev) && rev > 0 &&
                double.TryParse(Footprint, out double foot))
            {
                // TODO: 這裡的 R (車胎半徑) 與 K (輪胎特性常數) 需依照實際規格調整
                double tireRadius = 0.315; // 假設半徑 0.315 公尺
                double constantK = 15000;  // 實驗擬合常數 (需依據 AI 訓練結果調校)

                // L = 2 * PI * R * (T_F / T_R)
                double contactLength = 2 * Math.PI * tireRadius * (foot / rev);

                // Load = K * P * (T_F / T_R)
                double load = constantK * p * (foot / rev);

                EstimatedLoad = Math.Round(load, 1).ToString();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}