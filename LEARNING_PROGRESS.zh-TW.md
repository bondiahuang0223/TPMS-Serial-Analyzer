# 專案學習進度

最後更新：2026-08-31

## 目前狀態

- 專案：WPF + UART + TPMS 資料解析與 CSV 匯出。
- 已完成：第一次唯讀 code review、建立專案語法導覽與問題清單。
- 尚未修改產品程式碼；工作目錄原本已有未提交變更：`MainViewModel.cs`、`MainWindow.xaml.cs`。
- 建置：可重建，0 errors；仍有 nullable/未使用成員 warnings，詳見 `CODE_REVIEW.zh-TW.md`。

## 先讀這些文件

1. `PROJECT_GUIDE.zh-TW.md`：從 XAML、C# 語法到 UART 資料流。
2. `CODE_REVIEW.zh-TW.md`：已確認問題、優先順序與驗證結果。
3. `MVVM_ICommand_TUTORIAL.zh-TW.md`：既有的 Command/MVVM 教材。若顯示亂碼，請用 UTF-8 或確認原檔案編碼後再整理，不要直接覆寫。

## 下一次可從這裡開始

建議第一題：修正「清除 Log 時仍保留未完成 UART buffer」問題。

完成條件：

- 使用者按清除後，畫面 Log 與 CSV 緩衝均清空。
- `_rxBuffer` 也在正確鎖定範圍內清空。
- 下一筆 UART 資料不會與清除前的半行拼接。
- 重建無新增 warnings/errors。

下一題：決定並實作「顯示時間」開關行為，再修 nullable warnings。
