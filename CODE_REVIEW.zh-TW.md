# WpfMaterialHello Code Review

審查日期：2026-08-31  
範圍：目前工作目錄的程式碼，包含尚未提交的 `MainViewModel.cs` 與 `MainWindow.xaml.cs` 變更。此文件只記錄問題與建議，沒有改動產品程式碼。

## 驗證結果

- 使用 .NET SDK 對暫存輸出位置執行完整重建：**0 errors**。
- 重建輸出有 **54 warnings**；WPF 的暫存編譯專案與正式專案重複報告同一批警告，因此實質上是約 27 個原始碼警告。
- 正常 `dotnet build` 的預設輸出 `bin/Debug/net8.0-windows/WpfMaterialHello.exe` 正被執行中的程式鎖住；沒有關閉該程式，以免中斷使用者工作。
- 未發現自動化測試專案，因此尚未驗證實體 UART、裝置拔除與 CSV 檔案輸出的執行期行為。

## 優先修正

| 優先 | 問題 | 影響與證據 | 建議 |
| --- | --- | --- | --- |
| P0 | 清除 Log 沒有清空接收中的 `_rxBuffer` | `ExecuteClearLog` 只清除畫面與 CSV；若 UART 半行資料已在 buffer，下一段資料到來會和舊內容拼成一行。`MainViewModel.cs:254`、`MainWindow.xaml.cs:32` | 在清除命令路徑通知 SerialPort 擁有者，在同一把 `_bufferLock` 下清除 buffer；長期改為 SerialPort service 統一管理。 |
| P0 | 「顯示時間」開關已失效 | XAML 有 `ShowTimeToggle`，但接收流程在 `MainWindow.xaml.cs:198` 無條件加入時間戳；目前切換按鈕沒有讀取點。 | 決定規格：若要保留開關，將狀態繫結到 ViewModel 並在組合 Log 時判斷；若一律要時間，移除開關與文字。 |
| P1 | CSV 5 MB 後每一筆資料都會再發警告 | `_csvBuffer` 超過上限後每次 `AppendToCsvBuffer` 都呼叫 `OnLogMessageReceived`，高頻 UART 下會造成 Log 被警告洗掉。`MainViewModel.cs:194` | 加入 `bool _csvBufferFullWarningShown`，只告警一次；儲存或清除後重設。 |
| P1 | nullable 警告未清除 | `Nullable` 已啟用，卻有未初始化欄位/事件及介面簽章不符；會掩蓋日後真正的 null 缺陷。 | 使用 `?`、`string.Empty`、nullable event，以及正確的 `object?` / `EventHandler?` 簽章。不要用 `!` 靜默壓警告。 |
| P1 | `RelayCommand` 的 `CanExecuteChanged` 是空實作，`CanExecute` 永遠 true | 命令無法隨狀態停用；存檔/連線按鈕可在無效狀態被按。`RelayCommand.cs:10-12` | 實作可選 `Func<object?, bool>` 與 `RaiseCanExecuteChanged()`；在 COM 選擇、連線狀態與 CSV 資料數改變時觸發。 |
| P1 | COM/baud UI 狀態不一致 | 連線時只直接停用 `PortComboBox`，`BaudRateComboBox` 仍可改；UI 狀態散落在 code-behind。`MainWindow.xaml.cs:153`、`MainWindow.xaml:47` | 新增 `IsConnected` / `IsPortSelectionEnabled`，以 Binding 同時控制兩個 ComboBox。 |
| P2 | ViewModel 與 View 的職責交錯 | ViewModel 使用 `MessageBox` / `SaveFileDialog`，View 直接寫 TextBox/ComboBox 並管理 SerialPort；不易測試，也容易有狀態不同步。 | 保留現有功能，逐步抽出 `ISerialPortService`、`ILogFileService`、`IDialogService`。不要在修 P0 時同時全面重構。 |
| P2 | `ParseUARTString` 的執行緒契約不明確 | 現在正常路徑先用 `Dispatcher.InvokeAsync`，所以集合更新安全；但方法是 public，若未來背景執行緒直接呼叫，既有裝置屬性的通知可能在錯誤執行緒發生。 | 明定此方法只能在 UI 執行緒呼叫，或改成 service 在背景產生資料、ViewModel 統一透過 Dispatcher 更新。 |
| P2 | 裝置變更時仍可能刷新已連線選擇 | `WM_DEVICECHANGE` 一律呼叫 `RefreshPorts`；連線中的 Port 被拔除或新裝置出現時，`SelectedPort` 可能變成不同值。 | 連線中只記錄變更/提示，或保留原選擇並將連線狀態轉為錯誤。 |
| P3 | 無用成員與範本檔 | `OnClearLogRequested`、`OnSaveLogRequested` 不再被使用；`Class1`、`Class2`、`Interface1`、`Component1` 目前也無產品用途。 | 在 Git 提交前確認沒有外部使用，再刪除或寫明保留原因。 |
| P3 | 荷重計算有未使用變數及未校正常數 | `contactLength` 算出後未使用；`tireRadius`、`constantK` 是假設值。`TpmsDevice.cs:38-55` | 移除未使用變數，或顯示/紀錄接地長度；將常數移至可設定且附單位、校正依據的設定模型。 |

## 編譯警告分類

1. `CS8618`：非 nullable 欄位或事件在建構子結束時可能仍為 null。例如 `_serialPort`、`_selectedPort`、`TpmsDevice` 的字串欄位與各事件。
2. `CS8612` / `CS8767`：實作 `INotifyPropertyChanged`、`ICommand` 時，事件或 `object` 參數少了 `?`，與 .NET 介面宣告不一致。
3. `CS8625`：`string propertyName = null` 與 nullable 設定衝突，應是 `string? propertyName = null`。
4. `CS0067`：已不再訂閱/觸發的 `OnClearLogRequested`、`OnSaveLogRequested`。

## 非問題，但需要知道的限制

- CSV 欄位目前都從數字/MAC Regex 擷取，短期內不會有逗號跳脫問題；若日後加入原始文字欄位，必須使用標準 CSV escaping。
- `DataReceived` 不保證每次事件是一行資料。使用 `StringBuilder` 累積並依 `\n` 切行是正確的基本方向。
- `SerialPort` 裝置被拔除時可能在 `ReadExisting`、`Close`、`Dispose` 發生例外；需以硬體測試驗證錯誤 UI 和重連策略。
- 卡片上限 20 筆是以插入順序刪除最舊項目，而不是依最後更新時間排序；是否符合需求需先確認。

## 建議修正順序

1. 寫一個不依賴實體硬體的「清除時 buffer 為空」測試或最小手動再現步驟，修 P0 buffer 問題。
2. 決定時間顯示的產品規格，修 P0 開關問題。
3. 修 nullable 與未使用事件，讓建置警告為 0。
4. 修 CSV 滿額一次性告警與命令 `CanExecute`。
5. 以小步提交把 UART / 對話框責任抽到 service，補上 parser 與 CSV 的單元測試。
