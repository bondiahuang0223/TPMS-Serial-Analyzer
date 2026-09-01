# WpfMaterialHello 專案導覽與語法筆記

> 目的：這不是通用 C# 教科書，而是把本專案目前真正用到的 WPF、C#、UART 與 MVVM 語法逐一放回實際位置說明。閱讀程式時可搭配本文件。

## 1. 這個程式在做什麼

這是一個 .NET 8 WPF 桌面工具。它會列出 Windows 的 COM Port，依使用者選擇的 baud rate 開啟 UART，將每一行接收資料顯示在畫面上、用正規表示式解析 TPMS 資料、以 MAC 位址建立或更新輪胎卡片，並能匯出 CSV。

目前的主要資料流如下：

```text
UART 裝置
  -> SerialPort.DataReceived（背景執行緒）
  -> _rxBuffer：累積不完整資料、以 \n 切成完整行
  -> Dispatcher.InvokeAsync（回到 WPF UI 執行緒）
  -> MainViewModel.ProcessRealTimeData
       -> 顯示 Log 事件
       -> ParseUARTString：更新 Devices 中的 TpmsDevice
       -> AppendToCsvBuffer：寫入記憶體中的 CSV
  -> XAML Binding / 事件：更新卡片與 Log TextBox
```

一筆可被解析的資料範例：

```text
[14:32:01.123] MAC: AA:BB:CC:DD:EE:FF Pressure: 240 Temperature: 28 Voltage: 2500 Mileage: 1200 Cnt: 3 -65 dbm
```

同一顆裝置也可在另一筆資料提供 Format 2 欄位：

```text
[14:32:02.456] MAC: AA:BB:CC:DD:EE:FF Revolution: 500000 Footprint: 25000
```

兩行都以相同 MAC 對應到同一張 `TpmsDevice` 卡片，因此卡片會逐步補齊欄位。

## 2. 專案結構

| 檔案 | 實際責任 | 閱讀重點 |
| --- | --- | --- |
| `WpfMaterialHello.csproj` | 專案目標框架、NuGet 套件、WPF 開關 | `.NET 8`、`UseWPF`、PackageReference |
| `App.xaml` | 程式啟動點與全域 Material Design 樣式 | `StartupUri`、ResourceDictionary |
| `MainWindow.xaml` | 視窗排版與資料繫結 | `Grid`、`Binding`、`DataTemplate`、`Command` |
| `MainWindow.xaml.cs` | 視窗生命週期、Windows 訊息、SerialPort 接收 | `partial`、事件、執行緒、`Dispatcher` |
| `MainViewModel.cs` | UI 狀態、COM 清單、TPMS 解析、CSV 緩衝與命令 | `INotifyPropertyChanged`、集合、Regex |
| `TpmsDevice.cs` | 一張 TPMS 卡片的資料模型與低電壓/荷重計算 | 屬性 setter、副作用、通知 |
| `RelayCommand.cs` | 讓 XAML 的 `Command` 可呼叫 C# 動作 | `ICommand`、委派 |
| `Class1.cs`、`Class2.cs`、`Interface1.cs`、`Component1*.cs` | 目前未被主功能使用的 Visual Studio 範本檔 | 可在確認無引用後移除 |

`WpfMaterialHello.csproj` 的重要設定：

```xml
<OutputType>WinExe</OutputType>          <!-- 建立 Windows GUI 程式，不顯示主控台 -->
<TargetFramework>net8.0-windows</TargetFramework>
<Nullable>enable</Nullable>              <!-- 要求編譯器檢查 null 風險 -->
<UseWPF>true</UseWPF>                    <!-- 啟用 XAML/WPF 編譯工作 -->
```

套件 `MaterialDesignThemes` 提供 Material Design 控制項樣式；`System.IO.Ports` 提供 `SerialPort`。

## 3. WPF 的兩個檔案為什麼同名

`MainWindow.xaml` 定義畫面，`MainWindow.xaml.cs` 定義畫面的 C# 行為。兩者靠下列宣告組成同一個類別：

```xml
<Window x:Class="WpfMaterialHello.MainWindow">
```

```csharp
public partial class MainWindow : Window
```

- `partial`：同一個類別可拆在多個檔案。XAML 編譯後也會產生一部分程式碼。
- `: Window`：繼承 WPF 的 `Window` 基底類別，因此可覆寫 `OnClosed`、可使用 `Dispatcher`。
- `InitializeComponent()`：由 XAML 編譯器產生，負責建立控制項、套用資源、連接 `Loaded="Window_Loaded"` 等事件。它必須先於任何使用 `UartLogTextBox`、`PortComboBox` 的程式碼執行。

## 4. XAML：畫面如何描述

### 命名空間與資源

```xml
xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
```

- 預設 `xmlns` 讓 `<Grid>`、`<Button>` 等 WPF 控制項可用。
- `xmlns:x` 提供 XAML 語法，如 `x:Class`、`x:Name`、`x:Key`。
- `xmlns:materialDesign` 是 NuGet 套件的控制項命名空間，例如 `<materialDesign:Card>`。

`App.xaml` 的 `MergedDictionaries` 匯入套件資源；`BundledTheme` 設定 Light 主題及藍/琥珀色系。`DynamicResource` 則在執行期間從資源字典取得目前主題顏色。

### 排版控制項

- `Grid`：以列、欄定位。`Width="320"` 為固定寬，`Width="*"` 代表剩餘空間。
- `StackPanel`：依方向連續堆疊子控制項。此專案用於控制面板的水平排列與卡片內容的垂直排列。
- `ScrollViewer`：內容超過可見範圍時可捲動。
- `ItemsControl`：依資料集合重複產生項目；它本身沒有選取行為，適合單純顯示卡片。
- `DataTemplate`：告訴 `ItemsControl`「一個 `TpmsDevice` 要畫成什麼樣子」。

`Grid.Row`、`Grid.Column` 是附加屬性（attached property）：屬性實際上由 `Grid` 管理，卻寫在子控制項上。

### x:Name 與 Binding 的差別

```xml
<TextBox x:Name="UartLogTextBox" />
<TextBlock Text="{Binding ActionBtnText}" />
```

- `x:Name`：讓 code-behind 取得控制項實例，例如 `UartLogTextBox.AppendText(...)`。
- `{Binding ...}`：讓 XAML 從 `DataContext` 取得資料。本視窗的 `DataContext` 是 `_viewModel`，所以 `ActionBtnText` 會讀取 `MainViewModel.ActionBtnText`。

前者是直接操作 View；後者是資料驅動畫面。此專案目前兩種方式並存，這也是它屬於「部分 MVVM」而非完整 MVVM 的原因。

### 常見繫結

```xml
<ComboBox ItemsSource="{Binding AvailablePorts}"
          SelectedItem="{Binding SelectedPort}" />

<ItemsControl ItemsSource="{Binding Devices}">
    <TextBlock Text="{Binding Pressure, StringFormat='{}{0} kPa'}" />
</ItemsControl>
```

- `ItemsSource` 指向集合；集合每增加一項，畫面增加一個項目。
- `SelectedItem` 預設是雙向繫結，使用者選 COM Port 時會寫回 `SelectedPort`。
- DataTemplate 內的 DataContext 自動變成該列的 `TpmsDevice`，所以 `Pressure` 不必寫成 `device.Pressure`。
- `StringFormat='{}{0} kPa'`：`{0}` 是值的位置；前面的 `{}` 是避免 XAML 把字串當成另一個 markup extension。
- `BooleanToVisibilityConverter` 把 `bool` 轉為 `Visibility.Visible` 或 `Collapsed`，用於 `IsLowVoltage` 的告警區塊。

### Command

```xml
<Button Command="{Binding ClearLogCommand}" Content="清除" />
```

按鈕不需要 `Click="..."`。WPF 會呼叫 `ClearLogCommand.Execute(...)`。這讓按鈕的意圖留在 XAML、工作內容留在 ViewModel；若 `CanExecute` 回傳 `false`，WPF 可自動停用按鈕。

## 5. C# 基礎語法，對照本專案

### 命名空間、類別、欄位與建構子

```csharp
namespace WpfMaterialHello
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly object _bufferLock = new object();

        public MainViewModel()
        {
            RefreshPorts();
        }
    }
}
```

- `namespace`：避免不同函式庫有同名類別時衝突。
- `public`：其他類別可使用；`private`：只限本類別內使用。
- 欄位名稱以 `_` 開頭，是此專案採用的私有欄位慣例。
- `readonly`：欄位只能在宣告處或建構子賦值。鎖定物件不應被替換，因此適合 `readonly`。
- `new object()`：建立一個物件。這裡物件本身沒有資料意義，只作為 `lock` 的唯一識別物。
- 建構子與類別同名，建立實例時執行。

### 屬性（property）

```csharp
private int _selectedBaudRate = 256000;
public int SelectedBaudRate
{
    get => _selectedBaudRate;
    set { _selectedBaudRate = value; OnPropertyChanged(); }
}
```

屬性提供受控的讀寫入口。`get =>` 是 expression-bodied member（運算式主體）縮寫；`set` 在寫入後呼叫通知，讓 Binding 知道畫面需要重讀值。`{ get; set; }` 則是自動實作屬性，編譯器會產生隱藏欄位。

### 泛型集合

```csharp
ObservableCollection<TpmsDevice> Devices = new ObservableCollection<TpmsDevice>();
```

尖括號中的 `TpmsDevice` 是泛型型別參數，意思是這個集合只能存放 TPMS 裝置。`ObservableCollection<T>` 與 `List<T>` 的關鍵差異在於前者會送出「加入/移除」通知，WPF 可自動重畫清單。

### 介面與通知

`MainViewModel` 與 `TpmsDevice` 都實作 `INotifyPropertyChanged`。介面是合約：實作它的類別必須提供 `PropertyChanged` 事件。

```csharp
public event PropertyChangedEventHandler? PropertyChanged;

protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
```

- `event`：其他物件可訂閱，但只能由本類別觸發。
- `?.`：null conditional operator。若沒有訂閱者，略過 `Invoke`，不會拋出 NullReferenceException。
- `[CallerMemberName]`：呼叫端未傳名稱時，編譯器自動填入呼叫此方法的屬性名稱，例如 `"SelectedPort"`。
- `?`：nullable reference type，明確表示此參考可為 `null`。本專案已啟用 `<Nullable>enable</Nullable>`，因此應完整使用它。

### 委派、事件、Lambda

```csharp
public event Action<string>? OnLogMessageReceived;
OnLogMessageReceived?.Invoke(taggedLine + "\n");

_viewModel.OnLogMessageReceived += message =>
{
    UartLogTextBox.AppendText(message);
};
```

- `Action<string>` 是「接受一個 string、沒有回傳值」的委派型別。
- `+=` 訂閱事件；`-=` 解除訂閱。
- `message => { ... }` 是 lambda expression，將一個匿名方法指派給事件。
- 事件適合「發生了某件事」；屬性適合「目前狀態是什麼」。

### 條件、例外與資源釋放

```csharp
if (string.IsNullOrWhiteSpace(selectedPort)) return;

try
{
    _serialPort.Open();
}
catch (Exception ex)
{
    MessageBox.Show(ex.Message);
}
```

- `if` 依條件執行；`return` 直接結束目前方法。
- `try/catch` 處理可預期失敗，例如 COM Port 被其他程式佔用或裝置拔除。
- `SerialPort` 實作 `IDisposable`，必須 `Close()` / `Dispose()`。本專案在 `OnClosed` 處理，但後續應改成可重複安全呼叫的服務類別。

### 字串、StringBuilder 與插值

```csharp
string taggedLine = $"[{timeStamp}] {line}";
_csvBuffer.AppendLine($"{time},{mac},{pressure}");
```

- `$"..."` 是字串插值，花括號內求值後放進字串。
- `string` 是不可變（immutable）；大量 `+` 串接會不斷建立新字串。
- `StringBuilder` 適合逐行累積 Log/CSV，`AppendLine` 會附加平台換行字元。

### Regex、LINQ 與 out

```csharp
var macMatch = Regex.Match(line, @"([0-9a-fA-F]{2}(?::[0-9a-fA-F]{2}){5})");
var device = Devices.FirstOrDefault(d => d.Mac == mac);
bool valid = int.TryParse(Voltage, out int v);
```

- `var` 仍是靜態型別；編譯器從右側推得型別。此處分別是 `Match`、`TpmsDevice?`、`bool`。
- `@"..."` 是逐字字串，不把 `\` 視為跳脫字元，適合正規表示式。
- Regex 的 `{2}` 表示重複兩次、`(?:...)` 表示不捕捉的群組、`\s*` 表示零或多個空白。
- `FirstOrDefault` 是 LINQ：找第一個符合 lambda 條件的項目；找不到時參考型別結果為 `null`。
- `out int v` 讓 `TryParse` 在成功時把數值寫入 `v`，同時避免例外。

## 6. 執行緒：這個專案最需要理解的部分

WPF 控制項只能由建立它們的 UI 執行緒存取。`SerialPort.DataReceived` 卻在背景執行緒執行，因此不能直接寫 `UartLogTextBox` 或更新綁定集合。

```csharp
Dispatcher.InvokeAsync(() =>
{
    _viewModel.ProcessRealTimeData(taggedLine);
});
```

`Dispatcher` 是 UI 執行緒的工作佇列。`InvokeAsync` 把工作排回 UI 執行緒並立即返回，避免接收執行緒等待 UI。

`lock (_bufferLock)` 保護 `_rxBuffer`：同一時間只允許一個執行緒讀寫它。規則是：鎖內工作要短，不能在鎖內做檔案 I/O、顯示對話框或同步等待 UI。本專案目前在鎖內只切行與排程，方向正確，但可再把「切出完整行」與「派送 UI」分離來降低鎖持有時間。

## 7. 各核心類別的職責

### `MainWindow`

負責 View 特有事項：視窗建立/關閉、取得 HWND、監聽 `WM_DEVICECHANGE`、建立和關閉 `SerialPort`、將背景接收切換回 UI 執行緒。`WndProc` 是 Windows 原生訊息攔截器；`0x0219` 是 `WM_DEVICECHANGE`。

### `MainViewModel`

持有可繫結狀態：`AvailablePorts`、`SelectedPort`、`BaudRates`、`ActionBtnText`、`Devices`；也包含資料解析與 CSV 緩衝。它用 `RelayCommand` 把按鈕意圖轉成方法。

### `TpmsDevice`

代表一個 MAC 位址的最新量測值。每個 setter 都會發送變更通知；`Pressure`、`Revolution`、`Footprint` 改變時會再計算 `EstimatedLoad`。請注意現有荷重公式使用假設常數 `tireRadius = 0.315`、`constantK = 15000`，尚不是經實車校正的物理模型。

### `RelayCommand`

實作 WPF 的 `ICommand`：

```csharp
public void Execute(object? parameter) => _execute(parameter);
public bool CanExecute(object? parameter) => true;
```

目前它只是最小版本：所有命令都永遠可執行。完整版本應可接收 `Func<object?, bool>`，並在狀態改變時觸發 `CanExecuteChanged`，例如未連線或沒有 CSV 資料時停用相關按鈕。

## 8. 目前不是「完整 MVVM」：這不是錯，但要看得懂

目前分工是混合式：

```text
XAML -> Command -> MainViewModel -> 事件 -> MainWindow -> SerialPort
MainWindow -> Dispatcher -> MainViewModel -> 事件 -> MainWindow -> TextBox
MainViewModel -> MessageBox / SaveFileDialog
```

優點是容易一步步從事件式 WPF 過渡；缺點是 ViewModel 仍知道 WPF 對話框、View 仍直接改 `PortComboBox` 與 `UartLogTextBox`，使得單元測試與後續擴充困難。下一階段可把 UART 抽成 `ISerialPortService`，把存檔/對話框抽成 `ILogFileService` / `IDialogService`；但在了解現有資料流前，不要一次大規模重構。

## 9. 建議閱讀順序與練習方式

1. 先讀 `MainWindow.xaml`，認得每個控制項及它繫結的名稱。
2. 再讀 `MainViewModel` 中同名屬性，理解 UI 狀態如何改變。
3. 從 `ExecuteToggleConnection` 走到 `SerialPort_DataReceived`，追一次資料進來的路徑。
4. 以一筆上方範例資料，在 `ParseUARTString` 的每個 Regex 設中斷點，觀察 `Match.Success`、`Groups[1].Value` 與 `Devices`。
5. 最後讀 `TpmsDevice`，在 `Pressure` setter 和 `UpdateLoadEstimation` 看 `PropertyChanged` 如何讓卡片更新。

每次修改前，先寫下三件事：輸入是什麼、狀態要怎麼變、畫面預期怎麼反映。這比先讓 AI 寫一段程式再回頭猜用途，更能建立可驗證的理解。

## 10. 目前的已知問題

請閱讀 [CODE_REVIEW.zh-TW.md](CODE_REVIEW.zh-TW.md)。它列出已確認問題、風險與建議修正順序；其中最優先的是 Log 清除與接收緩衝的一致性、時間開關失效、CSV 滿額重複警告，以及 nullable 編譯警告。
