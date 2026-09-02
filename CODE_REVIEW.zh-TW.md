# WpfMaterialHello Code Review

## 2026-09-02 更新

- 執行 `dotnet build WpfMaterialHello.csproj --no-restore`：**0 errors、0 warnings**。
- 以下原始優先修正表完整保留，避免遺失既有 P1/P2/P3 的背景、影響與建議。
- 已完成：清除 Log 同步清除 `_rxBuffer`、時間顯示開關、CSV 滿額單次警告、nullable 警告、未使用事件、COM/baud 連線中停用、連線中不刷新已選 Port、RX buffer 上限，以及本輪 P1/P2。

### 本輪修正確認與保留事項

| 優先 | 問題 | 影響與證據 | 建議 |
| --- | --- | --- | --- |
| P1（已修正） | 剛連線時可能漏掉第一批 UART 資料 | `_serialPort` 現在會在 `Open()` 前指向 `tempPort`，因此剛開啟時的 `DataReceived` 可通過目前連線驗證。開啟失敗時會解除事件、Dispose，並清回 `_serialPort`。 | 已由程式碼審查與建置確認；仍建議以「開啟後只傳一次資料」的實體 UART 測試驗證。 |
| P2（已修正） | 正常斷線可能顯示「接收錯誤」對話框 | `ReadExisting()` 例外時，若 port 已關閉或不再是目前連線，會直接略過，不顯示錯誤對話框。 | 已由程式碼審查與建置確認；仍建議以快速斷線、重連與拔除裝置進行硬體驗證。 |
| P3（接受，不修改） | 3 處 trailing whitespace | `MainWindow.xaml.cs` 的行尾含空白字元。程式行為不受影響，但 `git diff --check` 會回報格式問題。 | 依目前決定保留，不作修改。 |

## 2026-08-31 原始優先修正（完整保留）

下表是原始審查的完整說明；項目是否已修正，以本文件頂端的 2026-09-02 更新為準。

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

## 原始限制與建議（保留）

- CSV 欄位目前都從數字/MAC Regex 擷取；日後若加入原始文字欄位，必須使用標準 CSV escaping。
- `DataReceived` 不保證每次事件是一行資料；使用 `StringBuilder` 累積並依 `\\n` 切行是正確的基本方向。
- `SerialPort` 裝置被拔除時可能在 `ReadExisting`、`Close`、`Dispose` 發生例外，仍需以硬體測試驗證錯誤 UI 和重連策略。
- 卡片上限 20 筆是以插入順序刪除最舊項目，而不是依最後更新時間排序；是否符合需求需先確認。
- 後續應補上 parser、CSV buffer 與連線狀態的單元測試；目前尚無自動化測試專案。
