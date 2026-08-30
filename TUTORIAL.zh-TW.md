# WpfMaterialHello：新手導覽

這是一個 Windows 桌面程式，用來透過序列埠（COM Port／UART）接收 TPMS（胎壓監測系統）文字資料。程式會把原始資料顯示在右側 Log，並依裝置的 MAC 位址在左側建立或更新一張輪胎資訊卡。

本文件的目標不是要求你一次看懂全部程式碼；建議先掌握「資料從哪裡來、經過哪些類別、最後怎麼變成畫面」這條路徑。

## 1. 執行前準備

* Windows 與 .NET 8 SDK。
* Visual Studio 2022（安裝「.NET desktop development」工作負載）或 Visual Studio Code 搭配 C# 擴充功能。
* 可選：一個 USB-to-UART 裝置或藍牙建立的虛擬 COM 埠，作為資料來源。

在專案根目錄執行：

```powershell
dotnet run
```

啟動後，選擇 COM Port 和 Baud Rate，再按「開啟連線」。預設 Baud Rate 是 `256000`；必須和硬體端的設定相同才會收到可用資料。

## 2. 專案地圖

| 檔案 | 負責什麼 |
| --- | --- |
| `WpfMaterialHello.csproj` | 專案設定、目標 .NET 版本與 NuGet 套件。 |
| `App.xaml` / `App.xaml.cs` | 應用程式入口與全域 Material Design 樣式。 |
| `MainWindow.xaml` | 主視窗的畫面結構與資料繫結規則。 |
| `MainWindow.xaml.cs` | 按鈕事件、序列埠連線、接收緩衝區與 Log 操作。 |
| `MainViewModel.cs` | 畫面需要的資料、COM 埠清單與 UART 文字解析。 |
| `TpmsDevice.cs` | 一個 TPMS 裝置的資料模型，以及低電壓／荷重計算。 |
| `logo.png` | 視窗右下角使用的資源圖片。 |
| `Class1.cs`、`Interface1.cs`、`Component1*.cs` | 目前未被主流程使用的 Visual Studio 範本檔，可先略過。 |
| `AssemblyInfo.cs` | WPF 主題資源搜尋設定，通常不需要在功能開發時修改。 |

`WpfMaterialHello.csproj` 中幾個值得認識的設定：

```xml
<TargetFramework>net8.0-windows</TargetFramework>
<UseWPF>true</UseWPF>
<PackageReference Include="MaterialDesignThemes" Version="5.3.2" />
<PackageReference Include="System.IO.Ports" Version="10.0.11" />
```

它們表示：程式目標是 Windows 的 .NET 8、啟用 WPF，並使用 Material Design 控制項樣式與 `SerialPort` 序列埠 API。

## 3. 先認識 WPF 的兩種檔案

一個 WPF 視窗通常由一組同名檔案組成：

* **`.xaml`**：用 XML 語法描述「畫面長什麼樣子」；例如按鈕、格線、文字框的位置與樣式。
* **`.xaml.cs`**：用 C# 描述「畫面怎麼動」；例如按下按鈕、收到資料、更新文字。

以主視窗為例：

```xml
<!-- MainWindow.xaml：宣告一個視窗與它的畫面 -->
<Window x:Class="WpfMaterialHello.MainWindow" ...>
```

```csharp
// MainWindow.xaml.cs：同一個 MainWindow 類別的另一部分
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
```

`partial` 的意思是「同一個類別可以分散寫在多個檔案」。編譯時，XAML 產生的程式與 `.xaml.cs` 會合成一個 `MainWindow` 類別。`InitializeComponent()` 會建立 XAML 中定義的控制項，讓 C# 可用 `ActionBtnText`、`UartLogTextBox` 等 `x:Name` 存取它們。

## 4. 程式從啟動到顯示資料的流程

```text
App.xaml
  └─ StartupUri="MainWindow.xaml" 建立主視窗
       └─ MainWindow 建立 MainViewModel，指定為 DataContext
            ├─ XAML 綁定 AvailablePorts / BaudRates / Devices
            └─ 使用者開啟 COM Port
                 └─ SerialPort.DataReceived（背景執行緒）寫入 _rxBuffer
                      └─ DispatcherTimer 每 500 ms（UI 執行緒）讀取完整行
                           ├─ 顯示到 UartLogTextBox
                           └─ MainViewModel.ParseUARTString()
                                └─ 新增或更新 TpmsDevice
                                     └─ XAML 自動更新對應卡片
```

這個分工很重要：背景執行緒只負責「快速收資料」；UI 執行緒再定期處理並改畫面，因此不會直接從背景執行緒碰 WPF 控制項。

## 5. `App.xaml`：全域外觀和起點

`StartupUri="MainWindow.xaml"` 指定程式啟動時要開啟主視窗。

`Application.Resources` 裡的 `MergedDictionaries` 則匯入 Material Design 的預設樣式，並用 `BundledTheme` 設成淺色、藍色主題、琥珀色輔色。因此在 `MainWindow.xaml` 使用 `MaterialDesignRaisedButton`、`materialDesign:Card` 時，控制項才會有一致的 Material Design 外觀。

`App.xaml.cs` 的 `App : Application` 目前沒有額外邏輯；它是 WPF 應用程式的 C# 類別入口。

## 6. `MainWindow.xaml`：怎麼讀 XAML 畫面

### 命名空間與視窗設定

```xml
<Window x:Class="WpfMaterialHello.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
        Loaded="Window_Loaded">
```

* `xmlns`：WPF 的標準控制項名稱空間，所以可以寫 `<Button>`、`<Grid>`。
* `xmlns:x`：XAML 本身的功能，例如 `x:Class`、`x:Name`、`x:Key`。
* `xmlns:materialDesign`：外部套件的名稱空間，所以可以寫 `<materialDesign:Card>`、`<materialDesign:PackIcon>`。
* `x:Class`：把這份 XAML 連到 `MainWindow.xaml.cs` 的 `MainWindow` 類別。
* `Loaded="Window_Loaded"`：視窗完成載入後，呼叫 C# 中同名的方法。

### 畫面配置

最外層 `Grid` 用兩欄版面：左欄固定 `320` 像素，右欄 `*` 代表吃掉所有剩餘空間。

* 左側上方：COM Port、Baud Rate、顯示時間開關與連線按鈕。
* 左側下方：`ScrollViewer` 包住 `ItemsControl`，用來垂直顯示每一台 TPMS 的卡片。
* 右側：UART 原始 Log、清除與儲存按鈕。

`Grid.Row`、`Grid.Column` 是附加屬性，用於指定控制項放在哪一格。`StackPanel` 則會把子控制項依 `Orientation` 水平或垂直排列。

### 資源與 Converter

```xml
<Window.Resources>
    <BooleanToVisibilityConverter x:Key="BoolToVis"/>
</Window.Resources>
```

這在視窗內建立一個可重複使用的轉換器。WPF 的 `Visibility` 需要 `Visible` 或 `Collapsed`，而 `TpmsDevice.IsLowVoltage` 是 `bool`；`BooleanToVisibilityConverter` 便把 `true` 轉成顯示、`false` 轉成隱藏。

### 資料繫結（Binding）

```xml
<ComboBox ItemsSource="{Binding AvailablePorts}"
          SelectedItem="{Binding SelectedPort}"/>
<ItemsControl ItemsSource="{Binding Devices}">
```

`{Binding 名稱}` 的意思是「到目前的 `DataContext` 找這個屬性」。`MainWindow` 的建構子把 `_viewModel` 指派給 `DataContext`，所以這些名稱對應 `MainViewModel`：

* `AvailablePorts`：COM 埠清單。
* `SelectedPort`：使用者所選 COM 埠。
* `BaudRates`、`SelectedBaudRate`：鮑率清單與選項。
* `Devices`：要顯示成卡片的 `TpmsDevice` 清單。

在卡片的 `DataTemplate` 內，資料來源會自動變成「目前那一筆 `TpmsDevice`」，因此 `{Binding Pressure}` 就是該裝置的 `Pressure` 屬性。

```xml
<TextBlock Text="{Binding Pressure, StringFormat='{}{0} kPa'}" />
```

`StringFormat` 會在壓力值後面補上 `kPa`。開頭的 `{}` 是 XAML 的跳脫寫法，讓接下來的 `{0}` 被當作格式字串而不是新的 Markup Extension。

事件寫法則和資料繫結不同：

```xml
<Button Click="ActionBtn_Click" />
```

使用者按下按鈕時，WPF 直接呼叫 `MainWindow.xaml.cs` 的 `ActionBtn_Click` 方法。

## 7. `MainWindow.xaml.cs`：連線、收資料與操作 UI

### 建構子與 DataContext

```csharp
InitializeComponent();
_viewModel = new MainViewModel();
DataContext = _viewModel;
```

順序不能顛倒：先由 `InitializeComponent()` 建立 XAML 控制項，再建立 ViewModel，最後建立資料繫結的資料來源。此檔也建立每 500 ms 執行一次的 `DispatcherTimer`。

### COM 埠的偵測與連線

`Window_Loaded` 會呼叫 `_viewModel.RefreshPorts()`。此外，`OnSourceInitialized` 透過 Windows 訊息 `WM_DEVICECHANGE` 偵測 USB／虛擬 COM 裝置插拔，再重新整理清單。

按下連線按鈕時：

1. 檢查已選擇有效的 COM 埠。
2. 建立 `SerialPort(selectedPort, SelectedBaudRate, Parity.None, 8, StopBits.One)`。
3. 訂閱 `DataReceived` 事件、呼叫 `Open()`、啟動 UI 定時器。
4. 更新按鈕文字並停用 Port 選單，避免連線中切換埠。

再次按按鈕則會停止定時器、取消事件訂閱並關閉連線。

### 為何要有 `_rxBuffer` 和 `lock`

`SerialPort_DataReceived` 在背景執行緒觸發，而且一次收到的資料不保證剛好是一整行。程式先用 `ReadExisting()` 讀取目前資料並加入 `StringBuilder _rxBuffer`。

`UiUpdateTimer_Tick` 在 UI 執行緒每 500 ms 找最後一個 `\n`，只取出完整行，保留尚未換行的殘段等待下次資料。兩個執行緒都可能讀寫 `_rxBuffer`，所以用：

```csharp
lock (_bufferLock)
{
    _rxBuffer.Append(newData);
}
```

`lock` 保證同一時間只有一個執行緒能使用這份共享緩衝區，避免資料交錯或遺失。

處理完整行後，程式會：

* 呼叫 `_viewModel.ParseUARTString(line)` 更新 TPMS 資料。
* 依「顯示時間」切換設定，附加時間戳或直接寫入 `UartLogTextBox`。
* 呼叫 `ScrollToEnd()` 讓 Log 停留在最新一行。

「清除」會同時清除畫面文字與背景緩衝區；「儲存 Log」透過 Windows 的 `SaveFileDialog` 讓使用者選擇 `.txt` 路徑，再用 `File.WriteAllText` 寫入。

## 8. `MainViewModel.cs`：畫面資料與封包解析

ViewModel 是畫面和工作邏輯之間的資料層。這裡使用的是輕量的 MVVM 做法：XAML 繫結到 `MainViewModel`，但按鈕 Click 事件仍放在 `MainWindow.xaml.cs`。

### `ObservableCollection<T>`

`AvailablePorts` 與 `Devices` 都是 `ObservableCollection<T>`，不是普通的 `List<T>`。新增、刪除或清空集合時，它會通知 WPF，因此 ComboBox 和卡片清單會自動重畫。

`RefreshPorts()` 使用 `SerialPort.GetPortNames()` 取得系統 COM 埠，清空並重建 `AvailablePorts`。若沒有埠，會顯示「未偵測COM Port」作為提示選項。

### `INotifyPropertyChanged`

`SelectedPort`、`SelectedBaudRate` 等一般屬性改變時，集合本身沒有變化，WPF 不會自動知道。因此 setter 內呼叫 `OnPropertyChanged()`：

```csharp
set
{
    _selectedPort = value;
    OnPropertyChanged();
}
```

這會發出「這個屬性已改變」通知，XAML 的 Binding 便更新。`[CallerMemberName]` 會自動帶入目前屬性名稱，所以通常不必手動寫字串。

### `ParseUARTString`

此方法一次處理一行 UART 文字。

1. 只有包含 `Pressure:` 或 `Revolution:` 的行才繼續處理。
2. 用正規表示式找出格式如 `AA:BB:CC:DD:EE:FF` 的 MAC 位址。
3. 在 `Devices` 中用 MAC 找既有裝置；沒有就建立新的 `TpmsDevice` 並加入集合。
4. 分別尋找並更新 `Pressure`、`Temperature`、`Voltage`、`Mileage`、`Revolution`、`Footprint`。

可被辨識的資料範例：

```text
MAC: AA:BB:CC:DD:EE:FF Pressure: 240 Temperature: 28 Voltage: 2500 Mileage: 1200
MAC: AA:BB:CC:DD:EE:FF Revolution: 500000 Footprint: 25000
```

格式 1 與格式 2 可分開到達；它們會因為 MAC 相同而更新同一張卡片。

## 9. `TpmsDevice.cs`：一張卡片背後的資料

一個 `TpmsDevice` 對應一個 MAC 位址與其顯示資料：胎壓、溫度、電壓、里程、轉動週期、輪胎印痕時間與最後更新時間。

每個會顯示的屬性都在設定值後呼叫 `OnPropertyChanged()`，所以卡片可以即時改變。例如 `Voltage` 除了通知自己，也通知衍生的 `IsLowVoltage`：

```csharp
public string Voltage
{
    set { _voltage = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsLowVoltage)); }
}
public bool IsLowVoltage => int.TryParse(Voltage, out int v) && v < 2300;
```

因此當電壓小於 `2300` mV 時，XAML 的 Converter 會讓紅色電池警告顯示出來。

`Pressure`、`Revolution`、`Footprint` 有任一更新時，也會呼叫 `UpdateLoadEstimation()`。在三個值皆能轉為數字且 `Revolution > 0` 時，程式使用：

```text
Load = K × Pressure × (Footprint / Revolution)
```

目前 `tireRadius = 0.315`、`constantK = 15000` 都是註解中明示的暫定／實驗值；實際荷重結果需要依輪胎規格與校正資料調整，不能直接當成量測保證值。

## 10. 新手最容易混淆的對照

| 需求 | 主要放在哪裡 | 範例 |
| --- | --- | --- |
| 改按鈕位置、字體、卡片外觀 | `.xaml` | `Margin`、`FontSize`、`Style`。 |
| 改按鈕點下去的行為 | `.xaml.cs` | `ActionBtn_Click`。 |
| 改下拉選單的資料或解析邏輯 | `MainViewModel.cs` | `RefreshPorts`、`ParseUARTString`。 |
| 新增一項裝置資料與卡片欄位 | `TpmsDevice.cs` + `MainWindow.xaml` | 新增屬性，再用 `{Binding 新屬性}` 顯示。 |
| 改啟動視窗或全域主題 | `App.xaml` | `StartupUri`、`BundledTheme`。 |

## 11. 安全地練習修改：新增 RSSI 欄位

想新增訊號強度（RSSI）可依序做：

1. 在 `TpmsDevice.cs` 新增帶有 `OnPropertyChanged()` 的 `Rssi` 屬性。
2. 在 `ParseUARTString` 新增 Regex，例如 `@"RSSI:\s*(-?\d+)"`，成功時寫入 `device.Rssi`。
3. 在卡片的 `DataTemplate` 新增：

```xml
<TextBlock Text="{Binding Rssi, StringFormat='RSSI: {0} dBm'}" />
```

4. 執行程式，送入含 `RSSI: -65` 的測試行，確認同一 MAC 的卡片會更新。

這正是 WPF 資料繫結的核心模式：**資料類別通知變更 → ViewModel 更新資料 → XAML 依 Binding 更新畫面**。

## 12. 後續可改善的方向

目前程式很適合作為學習與硬體測試工具。若要擴充成較完整的產品，可考慮：

* 將按鈕 Click 改為 `ICommand`，讓 MVVM 分層更完整。
* 在關閉視窗時自動停止 Timer、取消事件訂閱與關閉 `SerialPort`。
* 對不同裝置的數值範圍做驗證，並限制 Log 最大行數，避免長時間執行佔用太多記憶體。
* 將 UART 封包格式與荷重校正常數抽成設定檔或測試案例。

先看懂本文件的資料流程，再逐項做這些改動，會比一開始就重構整個專案更容易掌握。
