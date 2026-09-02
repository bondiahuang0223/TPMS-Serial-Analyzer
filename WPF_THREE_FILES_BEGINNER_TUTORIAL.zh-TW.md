# C# WPF 新手教程：讀懂 XAML、XAML Code-Behind 與 C# ViewModel

本文件以目前專案的三個核心檔案為教材，專為沒有接觸過 C# WPF 的同事撰寫。

| 核心檔案 | 在 WPF 的角色 | 最簡單的理解 |
| --- | --- | --- |
| MainWindow.xaml | View，畫面 | 決定畫面有什麼控制項、長什麼樣子、資料顯示在哪裡。 |
| MainWindow.xaml.cs | Code-behind，視窗行為 | 管理視窗生命週期、序列埠、Windows 訊息與 UI 執行緒。 |
| MainViewModel.cs | ViewModel，畫面資料與規則 | 提供可繫結資料、解析 UART、管理 CSV 與命令。 |

相關支援檔案：

- TpmsDevice.cs：一個 TPMS 裝置的資料模型。
- RelayCommand.cs：讓按鈕使用 Command 呼叫 C# 工作。
- App.xaml：程式啟動點與全域 Material Design 樣式。

本專案採用混合式 MVVM。主要資料採用 Binding、ObservableCollection 與 Command；但 SerialPort 與 TextBox 仍放在視窗程式碼。這樣很適合先學習資料流，之後再逐步重構為完整 MVVM。

## 1. 先看懂整個程式

這是一個讀取 UART 或 COM Port 的 TPMS 工具。它會將收到的文字顯示在右側 Log，並依 MAC 位址在左側建立或更新卡片。

範例輸入：

    MAC: AA:BB:CC:DD:EE:FF Pressure: 240 Temperature: 28 Voltage: 2500 Mileage: 1200

資料流程：

    使用者選擇 COM Port、按開啟連線
      -> XAML Button Command
      -> MainViewModel 的 ToggleConnectionCommand
      -> MainWindow.xaml.cs 的 ExecuteToggleConnection
      -> SerialPort.Open

    硬體送來資料
      -> SerialPort.DataReceived（背景執行緒）
      -> MainWindow.xaml.cs 的 _rxBuffer 切出完整行
      -> Dispatcher.InvokeAsync 回到 UI 執行緒
      -> MainViewModel.ProcessRealTimeData
      -> Log、TPMS 卡片與 CSV buffer 更新
      -> XAML Binding 自動重畫

每次閱讀或修改程式碼，先回答三件事：

1. 輸入是什麼？例如使用者選的 Port 或收到的一行 UART 文字。
2. 狀態放在哪裡？例如 SelectedPort、Devices、_csvBuffer。
3. 畫面為何更新？例如 Binding、PropertyChanged 或集合通知。

## 2. 如何建置與執行

在專案根目錄開啟 PowerShell：

    dotnet build
    dotnet run

需要 Windows 與 .NET 8 SDK。若有 USB-to-UART 或藍牙虛擬 COM Port，請選擇正確 Port 與 Baud Rate；預設 Baud Rate 為 256000，必須和硬體端相同。

沒有硬體仍可建置、閱讀與修改 UI；只是無法驗證資料接收。

# 第一部分：MainWindow.xaml

## 3. XAML 是什麼

XAML 是 WPF 用來描述桌面畫面的 XML 語法。它不是 HTML，也不在瀏覽器中執行；它會建立 Windows 桌面控制項。

    <Button Content="儲存 Log" Height="30" />

這會建立 Button 物件，並設定按鈕文字和高度。

XAML 基本規則：

    <!-- 註解：只給人看，不會執行 -->

    <!-- 沒有子項目的控制項可自我結束 -->
    <TextBlock Text="Hello" />

    <!-- 有子項目就要有開、關標籤 -->
    <StackPanel>
        <Button Content="第一個按鈕" />
        <Button Content="第二個按鈕" />
    </StackPanel>

父子關係就是畫面層級。上例的兩個按鈕都在 StackPanel 裡。

## 4. Window 如何對應到 C#

MainWindow.xaml 的開頭：

    <Window x:Class="WpfMaterialHello.MainWindow"
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
            Title="Initial Model"
            Loaded="Window_Loaded">

| 寫法 | 說明 |
| --- | --- |
| x:Class | 指定這份 XAML 對應的 C# 類別是 WpfMaterialHello.MainWindow。 |
| xmlns | 標準 WPF 控制項的來源，因此可以寫 Grid、Button、TextBox。 |
| xmlns:x | XAML 本身的語法來源，提供 x:Class、x:Name、x:Key。 |
| xmlns:materialDesign | NuGet 套件來源，因此能使用 materialDesign:Card 與 PackIcon。 |
| Loaded | 視窗載入完成時，呼叫 .xaml.cs 裡的 Window_Loaded 方法。 |

x:Class、C# 的 namespace 與 class 名稱必須一致，否則 XAML 無法編譯。

## 5. 排版控制項

最外層 Grid 用兩欄布局：

    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="320"/>
        <ColumnDefinition Width="*"/>
    </Grid.ColumnDefinitions>

- 320 是左欄固定寬度。
- 星號代表使用剩餘空間。

子控制項可以用 Grid.Column 放到第二欄：

    <materialDesign:Card Grid.Column="1">
        ...
    </materialDesign:Card>

Grid.Column 是附加屬性：寫在子控制項上，但由父層 Grid 解讀。

內層 Grid 又用兩列切出控制區與卡片區：

    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="*"/>
    </Grid.RowDefinitions>

- Auto：剛好包住內容。
- 星號：使用剩餘高度。

StackPanel 按順序排子控制項：

    <StackPanel Orientation="Horizontal">
        <ComboBox />
        <ComboBox />
    </StackPanel>

Orientation 為 Horizontal 時水平排列；預設是垂直排列。

ScrollViewer 讓內容超過視窗高度時可以捲動。ItemsControl 則負責依資料集合重複產生項目。

## 6. x:Name 與 Binding 的差別

    <TextBox x:Name="UartLogTextBox" />
    <TextBlock Text="{Binding ActionBtnText}" />

| 寫法 | 用途 | 本專案例子 |
| --- | --- | --- |
| x:Name | 讓 .xaml.cs 直接取得控制項物件。 | UartLogTextBox.AppendText。 |
| Binding | 告訴 WPF 從資料來源讀值。 | 顯示 ActionBtnText、Devices。 |

MainWindow 建構子中有：

    _viewModel = new MainViewModel();
    DataContext = _viewModel;

所以 Text 等於 Binding ActionBtnText 時，WPF 會讀取 MainViewModel.ActionBtnText。

Binding 的優點是 C# 改變資料後，畫面可自動更新；不必逐一尋找控制項再設定文字。

## 7. ComboBox 的資料繫結

    <ComboBox ItemsSource="{Binding AvailablePorts}"
              SelectedItem="{Binding SelectedPort}" />

- ItemsSource：下拉選單可選的項目。
- SelectedItem：目前選中的項目。

ComboBox 的 SelectedItem 預設會雙向繫結。使用者點選 COM3 後，MainViewModel.SelectedPort 會變成 COM3；程式改變 SelectedPort 時，ComboBox 也會跟著選取。

Baud Rate 使用同樣概念，只是項目是整數：

    <ComboBox ItemsSource="{Binding BaudRates}"
              SelectedItem="{Binding SelectedBaudRate}" />

## 8. Button Command

連線按鈕：

    <Button Command="{Binding ToggleConnectionCommand}">
        <TextBlock Text="{Binding ActionBtnText}" />
    </Button>

Command 表示按鈕不直接呼叫視窗方法，而是找 DataContext 中的 ToggleConnectionCommand。

傳統事件寫法：

    <Button Click="Button_Click" />

Command 寫法：

    <Button Command="{Binding ToggleConnectionCommand}" />

事件通常用於很小的視覺互動；Command 適合商業功能，因為它可以定義按鈕何時能按，也較容易測試。

## 9. ItemsControl 和 DataTemplate

    <ItemsControl ItemsSource="{Binding Devices}">
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <TextBlock Text="{Binding Pressure}" />
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>

外層的資料來源是 MainViewModel.Devices。DataTemplate 內的資料來源則自動變成集合中目前那一筆 TpmsDevice。

因此 Pressure 指向 TpmsDevice.Pressure，而不是 MainViewModel.Pressure。

    <TextBlock Text="{Binding Pressure, StringFormat='{}{0} kPa'}" />

StringFormat 把數值顯示為例如 240 kPa。前面的空大括號是 XAML 跳脫語法，避免它把 {0} 當作另一個 XAML 擴充語法。

## 10. Converter：用 bool 控制可見性

先在資源區建立 Converter：

    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVis"/>
    </Window.Resources>

再在卡片使用：

    <StackPanel Visibility="{Binding IsLowVoltage,
                Converter={StaticResource BoolToVis}}">
        <TextBlock Text="電壓過低" Foreground="Red" />
    </StackPanel>

TpmsDevice.IsLowVoltage 為 true 時顯示，為 false 時折疊隱藏且不占空間。

# 第二部分：MainWindow.xaml.cs

## 11. partial class 與繼承

    namespace WpfMaterialHello
    {
        public partial class MainWindow : Window
        {
        }
    }

| 語法 | 說明 |
| --- | --- |
| namespace | 類別的名稱空間，避免同名類別衝突。 |
| public | 其他程式碼可以使用這個類別。 |
| partial | 同一個類別可分在多個檔案；XAML 編譯產物會和這個檔案合併。 |
| : Window | 繼承 WPF Window，因此可覆寫 OnClosed、使用 Dispatcher。 |

## 12. using、欄位與 nullability

    using System.IO.Ports;
    using System.Text;
    using System.Windows;

using 讓程式可直接寫 SerialPort，而不必每次寫完整名稱 System.IO.Ports.SerialPort。

視窗的長期狀態放在欄位：

    private MainViewModel _viewModel;
    private SerialPort? _serialPort;
    private StringBuilder _rxBuffer = new StringBuilder();
    private readonly object _bufferLock = new object();

- private：僅 MainWindow 內可用。
- 底線開頭：本專案私有欄位慣例。
- SerialPort?：允許是 null；尚未連線時沒有 SerialPort。
- StringBuilder：適合持續累積 UART 文字。
- readonly：鎖定物件建立後不應更換。

## 13. 建構子與 DataContext

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.OnToggleConnectionRequested += ExecuteToggleConnection;
    }

順序不能任意交換：

1. InitializeComponent 讀取 XAML、建立控制項。
2. 建立 ViewModel。
3. 指定 DataContext，接通所有 Binding。
4. 訂閱 ViewModel 發出的連線請求事件。

加等號是訂閱事件；減等號是解除訂閱。

## 14. 事件與 Lambda

ViewModel 用事件通知 Log：

    public event Action<string>? OnLogMessageReceived;

視窗訂閱：

    _viewModel.OnLogMessageReceived += message =>
    {
        Dispatcher.InvokeAsync(() =>
        {
            UartLogTextBox.AppendText(message);
            UartLogTextBox.ScrollToEnd();
        });
    };

Action<string> 代表接受一個字串、沒有回傳值的方法。message => 是 Lambda Expression，意思是收到 message 時執行右邊工作。

## 15. UI 執行緒與 Dispatcher

SerialPort.DataReceived 由背景執行緒觸發；WPF 控制項卻只能由 UI 執行緒存取。背景執行緒直接改 TextBox 會造成跨執行緒例外。

正確做法：

    Dispatcher.InvokeAsync(() =>
    {
        _viewModel.ProcessRealTimeData(line, timeStamp);
    });

Dispatcher 是 UI 執行緒的工作佇列。InvokeAsync 把工作排入佇列並立即返回，因此不會卡住 UART 接收。

## 16. 為何需要 _rxBuffer

一次 DataReceived 不保證是一整行。例如：

    第一次：MAC: AA:BB:CC:DD:
    第二次：EE:FF Pressure: 240 加上換行

也可能一次收到多行。因此程式會：

    string newData = sp.ReadExisting();
    _rxBuffer.Append(newData);

    找到換行
    取出完整行
    從 _rxBuffer 移除完整行
    剩下未完成資料，等待下一次接收

每個完整行才交給 MainViewModel.ProcessRealTimeData。

## 17. lock：避免兩個執行緒同時改 buffer

背景接收事件與 UI 的清除 Log 都可能操作 _rxBuffer：

    lock (_bufferLock)
    {
        _rxBuffer.Clear();
    }

lock 保證同一時間只有一個執行緒能進入區塊，避免一邊切資料、一邊清除而資料錯亂。

鎖內工作應很短。不要在 lock 內寫檔、顯示對話框或等待長時間工作。

本專案也限制未換行 buffer 最多 4096 字元。裝置若一直傳無換行資料，程式會清除 buffer 並寫入警告，避免記憶體無限成長。

## 18. 開啟、關閉與釋放 SerialPort

簡化後的開啟流程：

    SerialPort tempPort = new SerialPort(selectedPort, baudRate);
    tempPort.DataReceived += SerialPort_DataReceived;
    _serialPort = tempPort;

    try
    {
        tempPort.Open();
    }
    catch
    {
        tempPort.DataReceived -= SerialPort_DataReceived;
        tempPort.Dispose();
        _serialPort = null;
    }

先訂閱事件、再開啟，避免剛連上時漏掉第一批資料。失敗時必須解除訂閱、釋放資源並清空欄位。

SafeCloseSerialPort 的責任是：

1. 解除 DataReceived 事件。
2. 若已開啟，呼叫 Close。
3. 呼叫 Dispose 釋放 OS 資源。
4. 將欄位設回 null。

USB 被拔除時 Close 或 Dispose 可能失敗，因此程式用 try、catch、finally 保護清理流程。

## 19. try、catch、finally

    try
    {
        tempPort.Open();
    }
    catch (Exception ex)
    {
        MessageBox.Show(ex.Message);
    }
    finally
    {
        // 不論成功、失敗或 return，都會執行
    }

- try：放可能失敗的工作。
- catch：處理例外。ex.Message 是錯誤原因。
- finally：不論結果都會執行，適合釋放資源。

不要把未知錯誤放進空 catch；只有明確知道可以忽略的情況才應忽略。

## 20. WM_DEVICECHANGE

Windows 在 USB 或虛擬 COM 裝置插拔時發出 WM_DEVICECHANGE。程式在 OnSourceInitialized 加入 Windows 訊息 Hook，然後由 WndProc 處理：

    if (msg == WM_DEVICECHANGE)
    {
        _viewModel.RefreshPorts();
    }

目前 RefreshPorts 在已連線時會直接返回，避免改寫正在使用的 SelectedPort。

# 第三部分：MainViewModel.cs 與支援 .cs

## 21. INotifyPropertyChanged

MainViewModel 宣告：

    public class MainViewModel : INotifyPropertyChanged

它表示這個類別承諾在資料改變時通知畫面。

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }

- event：其他物件可訂閱通知。
- 問號：事件或字串允許為 null。
- 問號加點 Invoke：沒人訂閱時不呼叫，避免 NullReferenceException。
- CallerMemberName：沒傳屬性名稱時，自動帶入呼叫者名稱。

## 22. 屬性與 backing field

    private string? _selectedPort;

    public string? SelectedPort
    {
        get { return _selectedPort; }
        set
        {
            _selectedPort = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

私有欄位保存真實值，public property 提供讀寫入口。setter 內呼叫 OnPropertyChanged，WPF 才知道 Binding 必須重新讀值。

若只寫自動屬性，畫面不會收到通知：

    public string? SelectedPort { get; set; }

## 23. ObservableCollection

    public ObservableCollection<string> AvailablePorts { get; set; }
    public ObservableCollection<TpmsDevice> Devices { get; set; }

List 只是清單；ObservableCollection 在 Add、Remove、Clear 時會通知 WPF。

    Devices.Add(device);

會使 ItemsControl 自動新增一張卡片。這只處理集合增減；集合內某台 TpmsDevice 的 Pressure 改變時，仍需要該物件自己的 PropertyChanged。

## 24. RelayCommand 與 CanExecute

RelayCommand 將一般方法包裝成 ICommand：

    public bool CanExecute(object? parameter)
        => _canExecute == null || _canExecute(parameter);

    public void Execute(object? parameter)
        => _execute(parameter);

建立連線命令：

    ToggleConnectionCommand = new RelayCommand(
        _ => OnToggleConnectionRequested?.Invoke(),
        _ => ActionBtnText == "關閉連線"
             || (!string.IsNullOrWhiteSpace(SelectedPort)
                 && !SelectedPort.Contains("未偵測")));

第一個 Lambda 定義按下時的工作；第二個定義可否按下。底線表示不使用 Command 傳入的 parameter。

當 SelectedPort 改變時，InvalidateRequerySuggested 會要求 WPF 重問 CanExecute，按鈕的可用狀態隨之更新。

## 25. ProcessRealTimeData：一行資料的三個去處

    public void ProcessRealTimeData(string rawLine, string timeStamp)
    {
        顯示 Log
        ParseUARTString
        AppendToCsvBuffer
    }

這是閱讀 MainViewModel 最好的入口。一筆完整 UART 資料會：

1. 依 IsShowTime 決定 Log 是否顯示時間。
2. 觸發 OnLogMessageReceived，由 View 把文字加入 TextBox。
3. 呼叫 ParseUARTString 更新 TPMS 卡片。
4. 呼叫 AppendToCsvBuffer 累積 CSV。

字串插值使用美元符號加雙引號；大括號中的變數值會被放入字串。

## 26. Regex 與 LINQ

找 MAC 位址：

    var macMatch = Regex.Match(
        line,
        @"([0-9a-fA-F]{2}(?::[0-9a-fA-F]{2}){5})");

- var：由右側推論型別；這裡是 Match。
- at 符號加雙引號：逐字字串，適合 Regex。
- {2}：前面規則重複兩次。
- 非捕捉群組加 {5}：重複五次冒號加兩個十六進位字元。

找同 MAC 的裝置：

    var device = Devices.FirstOrDefault(d => d.Mac == mac);

FirstOrDefault 是 LINQ，找第一個符合條件的項目；找不到時結果為 null。d 箭頭是 Lambda，表示集合裡的每一個裝置 d。

## 27. TpmsDevice：一張卡片的資料

    public string? Pressure
    {
        get => _pressure;
        set
        {
            _pressure = value;
            OnPropertyChanged();
            UpdateLoadEstimation();
        }
    }

Pressure、Voltage、Mileage 等屬性都在設定後通知畫面。Voltage 還會通知衍生的 IsLowVoltage：

    OnPropertyChanged(nameof(IsLowVoltage));

nameof 比手寫字串安全；日後重新命名屬性時，編譯器可以協助檢查。

IsLowVoltage 沒有自己的欄位，而是從 Voltage 計算：

    public bool IsLowVoltage =>
        int.TryParse(Voltage, out int v) && v < 2300;

當電壓小於 2300 mV，XAML 的 BooleanToVisibilityConverter 會讓紅色警告出現。

Pressure、Revolution、Footprint 更新時會重算 EstimatedLoad。公式使用的輪胎半徑與 K 值是暫定校正常數，不能當作已驗證的物理量測結果。

## 28. CSV buffer 和存檔

MainViewModel 使用 StringBuilder 保存 CSV：

    Time,MAC,Pressure(kPa),Temp(C),Voltage(mV),...
    14:32:01.123,AA:BB:CC:DD:EE:FF,240,28,2500,...

重點：

- CSV 緩衝上限約 5 MB。
- 滿額只警告一次；清除後重新開始記錄。
- SaveFileDialog 讓使用者選位置。
- File.WriteAllText 寫入 CSV。

在完整 MVVM 中，對話框與檔案存取通常會抽成服務。這個範例暫時放在 ViewModel，目的是讓初學者先專注於資料流。

# 第四部分：練習與除錯

## 29. 練習：新增 RSSI 顯示

這是理解三檔案分工最好的練習。

第一步，在 TpmsDevice.cs 新增屬性：

    private string? _rssi;
    public string? Rssi
    {
        get => _rssi;
        set
        {
            _rssi = value;
            OnPropertyChanged();
        }
    }

第二步，在 MainViewModel.ParseUARTString 找 RSSI：

    var rssiMatch = Regex.Match(line, @"(-\d+)\s*dbm");
    if (rssiMatch.Success)
        device.Rssi = rssiMatch.Groups[1].Value;

第三步，在 MainWindow.xaml 的 DataTemplate 顯示：

    <TextBlock Text="{Binding Rssi,
                      StringFormat='RSSI: {0} dBm'}" />

最後建置：

    dotnet build

送入包含 -65 dbm 的資料，確認同 MAC 的卡片顯示 RSSI。這正是資料模型通知、ViewModel 更新、XAML Binding 重畫的完整循環。

## 30. 常見錯誤

| 現象 | 常見原因 | 優先檢查 |
| --- | --- | --- |
| Binding 顯示空白 | DataContext 未設定、屬性拼錯或不是 public。 | 建構子的 DataContext。 |
| 值改了但畫面沒變 | 忘了 OnPropertyChanged。 | property setter。 |
| 新增資料卻沒新卡片 | 使用 List 而非 ObservableCollection。 | Devices 型別與 ItemsSource。 |
| 收資料時跨執行緒例外 | 背景執行緒直接改 WPF 控制項。 | Dispatcher.InvokeAsync。 |
| 可開 COM Port 卻無資料 | Baud Rate 不符、硬體未送換行、Port 被占用。 | 硬體設定與 _rxBuffer。 |
| 按鈕無法按 | CanExecute 回傳 false。 | SelectedPort 與命令規則。 |
| XAML 無法編譯 | 標籤或引號未關閉、x:Class 不符。 | 錯誤訊息指定的 XAML 行。 |

## 31. 建議閱讀順序

1. 先讀 MainWindow.xaml，找出 COM 選單、連線按鈕、卡片和 Log。
2. 從 XAML 找一個 Binding，例如 ActionBtnText，再搜尋 MainViewModel 中同名屬性。
3. 從 ToggleConnectionCommand 跟到 ExecuteToggleConnection。
4. 從 SerialPort_DataReceived 跟到 ProcessRealTimeData。
5. 最後讀 ParseUARTString 與 TpmsDevice。
6. 完成 RSSI 練習。

## 32. 最後的記憶口訣

    XAML：畫面要呈現什麼
    XAML.cs：視窗和系統資源怎麼互動
    ViewModel.cs：畫面資料和規則怎麼改變
    Model：一筆資料有哪些欄位
    Binding：資料改了，畫面跟著改

每次修改前先寫下：

    輸入：收到 RSSI -65
    狀態：TpmsDevice.Rssi 變成 -65
    畫面結果：對應卡片顯示 RSSI -65 dBm

能清楚回答輸入、狀態與畫面結果，就能安全地逐步擴充這個 WPF 專案。
