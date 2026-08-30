# 將 Button Click 改為 `ICommand`：WPF MVVM 新手教學

本專案目前在 `MainWindow.xaml` 使用事件：

```xml
<Button Click="ActionBtn_Click">...</Button>
```

按下按鈕後，WPF 會直接呼叫 `MainWindow.xaml.cs` 的 `ActionBtn_Click`。這種寫法能正常運作，也很適合小型練習；不過視窗類別會同時負責畫面、序列埠連線、Log 與對話框，功能增多後會越來越難測試與維護。

本文件說明如何改為 `ICommand`，讓 XAML 只描述畫面，ViewModel 決定按鈕能否按與按下後要做的事。

> 本文件是重構說明；目前專案程式碼尚未套用這項改動。

## 1. 先理解事件與 Command 的差別

```text
現況：Button --Click--> MainWindow.xaml.cs --直接操作控制項-->

目標：Button --Command Binding--> MainViewModel --更新屬性／呼叫服務-->
                                      |
                                      └-- PropertyChanged --> XAML 自動重畫
```

| 類型 | XAML 寫法 | 邏輯放在哪裡 | 適合情況 |
| --- | --- | --- | --- |
| 事件（Event） | `Click="ActionBtn_Click"` | `.xaml.cs` | 很小、一次性的畫面互動。 |
| 命令（Command） | `Command="{Binding ToggleConnectionCommand}"` | ViewModel | 商業功能、需要啟用／停用規則、需要單元測試。 |

`ICommand` 是 .NET 的介面，它有三個重點：

```csharp
bool CanExecute(object? parameter); // 現在可不可以執行？
void Execute(object? parameter);    // 按下後做什麼？
event EventHandler? CanExecuteChanged; // 規則改變時通知按鈕重問一次
```

WPF 會自動用 `CanExecute` 決定按鈕的 `IsEnabled`。所以不需要再寫 `PortComboBox.IsEnabled = false` 這種直接抓 UI 控制項的程式。

## 2. 這個專案的重構目標

目前有三個 Click 事件：

| 現有事件 | 對應 Command | ViewModel 應管理的畫面狀態 |
| --- | --- | --- |
| `ActionBtn_Click` | `ToggleConnectionCommand` | `IsConnected`、`ActionButtonText`、`IsPortSelectionEnabled`。 |
| `ClearLogBtn_Click` | `ClearLogCommand` | `UartLog`、是否可清除。 |
| `SaveLogBtn_Click` | `SaveLogCommand` | `UartLog`、是否可儲存。 |

此外，現在的 `UartLogTextBox.AppendText(...)` 和 `ActionBtnText.Text = ...` 必須改為更新 ViewModel 屬性。XAML 已經很擅長顯示 Binding 的值，不需要由 C# 直接碰控制項。

## 3. 第一步：建立可重複使用的 `RelayCommand`

新增 `RelayCommand.cs`。它把委派（方法）包裝成 `ICommand`，讓每個按鈕都不必重複實作介面。

```csharp
using System;
using System.Windows.Input;

namespace WpfMaterialHello;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
        => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
        => _execute(parameter);

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
```

讀法如下：

* `Action<object?>` 是「沒有回傳值的工作」；例如連線或清除 Log。
* `Func<object?, bool>` 是「判斷是否可以做」；例如 Log 是否為空。
* `RaiseCanExecuteChanged()` 是通知按鈕重新呼叫 `CanExecute()`。

這是教學用的最小版本。若日後加入非同步連線，應再建立 `AsyncRelayCommand`，避免在 UI 執行緒中做長時間工作。

## 4. 第二步：把畫面狀態放進 `MainViewModel`

在 `MainViewModel.cs` 新增下列屬性。每個會影響畫面的值都需要在 setter 內呼叫既有的 `OnPropertyChanged()`。

```csharp
using System.Windows.Input;

private bool _isConnected;
public bool IsConnected
{
    get => _isConnected;
    private set
    {
        if (_isConnected == value) return;
        _isConnected = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(ActionButtonText));
        OnPropertyChanged(nameof(IsPortSelectionEnabled));
    }
}

public string ActionButtonText => IsConnected ? "關閉連線" : "開啟連線";
public bool IsPortSelectionEnabled => !IsConnected;

private string _uartLog = string.Empty;
public string UartLog
{
    get => _uartLog;
    private set { _uartLog = value; OnPropertyChanged(); }
}

public ICommand ToggleConnectionCommand { get; }
public ICommand ClearLogCommand { get; }
public ICommand SaveLogCommand { get; }
```

注意 `ActionButtonText` 是「計算屬性」：它沒有自己的欄位，直接由 `IsConnected` 算出文字。當 `IsConnected` 改變時，必須也通知 `ActionButtonText`，否則 XAML 不知道要重畫。

若只要先練習清除功能，可在建構子先建立兩個命令：

```csharp
public MainViewModel()
{
    AvailablePorts = new ObservableCollection<string>();
    RefreshPorts();

    ClearLogCommand = new RelayCommand(_ => ClearLog(), _ => !string.IsNullOrEmpty(UartLog));
}

public void AppendLog(string line)
{
    UartLog += line + Environment.NewLine;
    ((RelayCommand)ClearLogCommand).RaiseCanExecuteChanged();
}

private void ClearLog()
{
    UartLog = string.Empty;
    ((RelayCommand)ClearLogCommand).RaiseCanExecuteChanged();
}
```

`AppendLog` 是 ViewModel 對外提供的意圖：加入一行 Log。日後任何來源（UART、測試、模擬器）都能呼叫它，不必知道畫面上有一個 `TextBox`。

## 5. 第三步：把 XAML 的 `Click` 改為 `Command`

### 連線按鈕

原本：

```xml
<Button x:Name="ActionBtn" Click="ActionBtn_Click">
    <TextBlock x:Name="ActionBtnText" Text="開啟連線" />
</Button>
```

改為：

```xml
<Button Style="{StaticResource MaterialDesignRaisedButton}"
        Command="{Binding ToggleConnectionCommand}">
    <StackPanel Orientation="Horizontal">
        <materialDesign:PackIcon Kind="SerialPort" Width="24" Height="24" />
        <TextBlock Text="{Binding ActionButtonText}" />
    </StackPanel>
</Button>
```

這裡應移除 `x:Name="ActionBtn"` 和 `x:Name="ActionBtnText"`，因為 C# 不再直接取用它們。

### COM Port 選單與 Log

```xml
<ComboBox ItemsSource="{Binding AvailablePorts}"
          SelectedItem="{Binding SelectedPort}"
          IsEnabled="{Binding IsPortSelectionEnabled}" />

<Button Content="清除"
        Command="{Binding ClearLogCommand}" />

<Button Content="儲存 Log"
        Command="{Binding SaveLogCommand}" />

<TextBox Text="{Binding UartLog}"
         IsReadOnly="True"
         TextWrapping="Wrap"
         VerticalScrollBarVisibility="Auto" />
```

同樣地，移除三個按鈕的 `Click="..."`，並移除 `ClearLogBtn`、`SaveLogBtn`、`UartLogTextBox` 的 `x:Name`（沒有其他地方使用時）。`TextBox.Text` 預設是雙向繫結；因為它是唯讀 Log，也可明確寫成 `Text="{Binding UartLog, Mode=OneWay}"`，表達「只有資料更新畫面」。

## 6. 第四步：連線功能不能只搬方法

把目前 `ActionBtn_Click` 內容直接貼到 `MainViewModel`，雖然能讓 `Command` 工作，卻仍讓 ViewModel 依賴 `MessageBox`、`SerialPort`、`DispatcherTimer` 等視窗／系統細節。這是「有 Command，但 MVVM 尚未完整」的狀態。

較乾淨的分層如下：

```text
MainWindow.xaml
  └─ Binding Command / Property
MainViewModel
  ├─ 決定連線、斷線、清除等使用案例
  └─ 依賴抽象介面（ISerialPortService、ILogFileService）
服務類別
  ├─ SerialPortService：真正操作 SerialPort 與資料接收
  └─ LogFileService：真正顯示 SaveFileDialog、寫入檔案
```

### 序列埠服務介面

新增 `ISerialPortService.cs`，只描述 ViewModel 需要的能力，不描述實作細節：

```csharp
public interface ISerialPortService : IDisposable
{
    bool IsOpen { get; }
    event EventHandler<string>? LineReceived;
    void Open(string portName, int baudRate);
    void Close();
}
```

`SerialPortService` 再把現有 `_serialPort`、`_rxBuffer`、`lock`、換行切割與 Timer 邏輯搬入。收到完整行後觸發 `LineReceived`。這可讓 `MainViewModel` 完全不知道 `_rxBuffer` 怎麼做，只負責：

```csharp
private void OnLineReceived(object? sender, string line)
{
    ParseUARTString(line);
    AppendLog(line);
}
```

> `LineReceived` 若從背景執行緒觸發，更新 `ObservableCollection` 和 `UartLog` 前仍需切回 UI 執行緒。可由服務提供 UI 執行緒事件，或在 ViewModel 注入 `Dispatcher`／同步內容後轉送。不要在背景執行緒直接改 WPF 綁定集合。

### 儲存服務介面

存檔目前同時使用 `SaveFileDialog` 和 `File.WriteAllText`。可抽為：

```csharp
public interface ILogFileService
{
    void Save(string content);
}
```

`SaveLogCommand` 只需傳入 `UartLog`；實作類別決定顯示對話框、檔案名稱與寫檔失敗時如何回報。若希望 ViewModel 完全不依賴 WPF，訊息提示也應經由另一個 `IDialogService` 處理，而不是呼叫 `MessageBox.Show`。

## 7. `ToggleConnectionCommand` 的範例邏輯

完成服務後，ViewModel 建構子可接收服務並建立 Command：

```csharp
private readonly ISerialPortService _serialPortService;
private readonly RelayCommand _toggleConnectionCommand;

public ICommand ToggleConnectionCommand => _toggleConnectionCommand;

public MainViewModel(ISerialPortService serialPortService)
{
    _serialPortService = serialPortService;
    _serialPortService.LineReceived += OnLineReceived;

    _toggleConnectionCommand = new RelayCommand(
        _ => ToggleConnection(),
        _ => IsValidPortSelected());
}

private bool IsValidPortSelected()
    => !string.IsNullOrWhiteSpace(SelectedPort)
       && !SelectedPort.Contains("未偵測");

private void ToggleConnection()
{
    if (_serialPortService.IsOpen)
    {
        _serialPortService.Close();
        IsConnected = false;
        AppendLog("--- 已中斷連線 ---");
        return;
    }

    _serialPortService.Open(SelectedPort, SelectedBaudRate);
    IsConnected = true;
    AppendLog($"--- 已連線至 {SelectedPort} ---");
}
```

在 `SelectedPort` setter 的最後也要通知命令重新檢查：

```csharp
_toggleConnectionCommand.RaiseCanExecuteChanged();
```

但建構子執行前命令尚未建立，實作時可把命令欄位設為 nullable 後檢查，或在先建立命令、後呼叫 `RefreshPorts()` 的順序下避免這個問題。這是初學者常遇到的初始化順序問題。

## 8. 主視窗最後應剩下什麼？

重構後的 `MainWindow.xaml.cs` 應非常薄，主要是建立依賴並設定 `DataContext`：

```csharp
public MainWindow()
{
    InitializeComponent();

    var serialPortService = new SerialPortService();
    var logFileService = new LogFileService();
    DataContext = new MainViewModel(serialPortService, logFileService);
}
```

這種在視窗裡 `new` 服務的做法適合第一步重構。專案變大後，可再導入 .NET 的 DI 容器，統一管理服務建立與釋放。

仍可保留少數真正屬於 View 的行為在 `.xaml.cs`，例如：視窗拖曳、純動畫、焦點控制、Log 自動捲到最底端。原則是：**只要沒有業務決策、也不需要測試，就可以留在 View；否則放 ViewModel 或服務。**

## 9. 建議的實作順序與驗收

不要一次搬完 UART 邏輯。請依以下小步驟進行，每一步都建置與手動測試：

1. 新增 `RelayCommand`，只將「清除 Log」改成 `ClearLogCommand`。
2. 將 Log 文字改為 `UartLog` Binding，確認清除按鈕會在空 Log 時自動停用。
3. 改「儲存 Log」為 Command，抽出 `ILogFileService`。
4. 將連線按鈕文字、選單啟用狀態改成 ViewModel 屬性。
5. 最後抽出 `ISerialPortService`，搬移 `SerialPort`、Timer、Buffer 與事件訂閱。
6. 關閉視窗時呼叫服務的 `Dispose()`，確保 COM Port 釋放。

每一步的驗收重點：按鈕是否依條件啟用、連線與斷線後文字是否正確、UART 行是否仍能新增卡片及 Log、儲存與清除是否與原先相同。

## 10. 常見錯誤

* **只宣告 `ICommand`，沒在建構子初始化**：按鈕會沒有可執行的命令。
* **忘記呼叫 `RaiseCanExecuteChanged()`**：資料已變，但按鈕仍是舊的啟用狀態。
* **忘記 `OnPropertyChanged(nameof(ActionButtonText))`**：`IsConnected` 已變，按鈕文字卻不變。
* **把 `TextBox.AppendText` 留在 ViewModel**：ViewModel 又知道特定 UI 控制項，分層效果被破壞。
* **背景執行緒直接改 `Devices`**：可能拋出跨執行緒例外；必須切回 UI 執行緒。
* **為了 MVVM 硬搬所有 View 行為**：不是所有 `.xaml.cs` 都是錯的；純視覺互動可留在 View。

完成這個重構後，XAML 不再知道「按鈕要呼叫哪個視窗方法」，`MainViewModel` 也不再知道「按鈕或文字框叫什麼名字」。兩者只透過 Command 與 Binding 溝通，這就是 MVVM 分層帶來的主要價值。
