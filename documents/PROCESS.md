# PROCESS.md — 我的練習心得

> 一個原則：**寫「具體發生的事」，不寫感想文。**
> 貼上當時真實的 prompt、真實的數字、真實的錯誤訊息——三個月後的你（和你的同事）才用得上。

#### 使用的 agent 與模型：

Claude Code (Sonnet 5)

---

## 通用四問

### 1. 我的任務拆解

一開始以為「setup」就是把網站跑起來（README 的啟動步驟），做完 build/test/run 驗證後才發現這只是前置作業，「練習 1」實際上是另一件事——設定 agent 本身（CLAUDE.md、settings.json、hooks、subagents、skill）。順序變了：原本以為 task 1 = 環境跑起來，後來才拆成「環境跑起來」→「設定 agent 五個檔案」→「逐項驗證每個設定真的生效」→「commit」→「PROCESS.md 自我驗證」五步。中間也因為 git identity 沒設定卡了一次，commit 失敗後才補上 `git config`。

### 2. AI 幫上大忙的地方

環境診斷：直接查了 `dotnet --list-sdks`、SQL Server 服務清單、登錄檔的具名 instance，才發現 appsettings.json 預設的 `Server=localhost` 根本連不上（機器上只有 SQLEXPRESS / MSSQLSERVERTHIRD 具名實例 + LocalDB，沒有預設實例）。這種「先假設會動，出事才debug」跟「先掃過環境再動手」的差異，省了一次「dotnet run 却連不上 DB」的挫折。

逐條驗證 permission/hook 設定：沒有只讀設定檔就假設它會生效，而是實際跑 `git push --force`（被 deny 擋下）、`dotnet ef database drop`（跳出 ask 確認）、用 sqlcmd 送 `TRUNCATE`（被 PreToolUse hook 擋下）、寫一個 sample.txt（PostToolUse hook 有記錄到 edit-log.txt）——每一條都是實際觸發、看到真實行為，不是憑印象確認。

### 3. AI 誤導我的地方，與我如何發現

請 agent 描述「建單流程」，它講出兩處後來證實是錯的：
1. 「折扣只在小計上算一次」——實際讀 `OrderService.CreateOrderAsync` 才發現 Gold 會員的單價快照在建單當下就先打過一次 9 折，`CalculateTotal` 又對小計整個再打一次折，等於折兩次（0.9×0.9）。Silver 沒有這段快照時折扣，所以正常。
2. 「取消訂單會把庫存加回去」——實際讀 `CancelOrderAsync` 才看到 `order.Status = OrderStatus.Cancelled` 這行**先**執行，接下來判斷「還原庫存」的 `if (order.Status == Pending || Confirmed)` 因為狀態已經被改成 Cancelled，永遠不會成立，還原庫存那段是死碼。

發現方式：不是靠讀 agent 的文字說明，而是直接把 `OrderService.cs`、`OrdersController.cs` 整份讀完、逐行對照它的描述。兩處都是真的會影響金額和庫存正確性的地方，如果照單全收會直接漏掉兩個 bug。

### 4. 我會帶回日常工作的一招

**先讓 agent 口頭描述一遍流程，再自己去讀對應的原始碼逐行核對，而不是先讀 agent 的結論再決定信不信。** 具體做法：問 agent「你覺得這段流程是怎麼運作的？」，拿到答案後不做任何評論，直接開對應的 service/controller 檔案，一行一行比對它講的每一句話，特別注意「只做一次」「應該會 xxx」這類肯定語氣的敘述——這類語句最容易藏著沒驗證過的假設。

---

## 自我驗證（做到哪個階段答哪題）

### 第一階段 — Agentic Coding

練習 1

1. [o] 我能不看筆記說出三個專案（Web/Core/Infrastructure）各自的職責
2. [o] 我核對過 agent 描述的建單流程，且至少找出一處不精確或過度簡化的說法（實際找出兩處：Gold 折扣算兩次、取消訂單庫存還原是死碼）
3. [o] 我知道商業邏輯應該放在哪一層、新增頁面要動哪些地方（商業邏輯在 Core service；DB 查詢只能在 Infrastructure repository；新增頁面要動 Controller、Service、Repository、ViewModel、View、測試）

練習 2

1. 三個 bug 我都先在頁面上重現過，才開始找程式
2. 我給 agent 的資訊包含具體觀察（頁碼／金額數字／庫存數字），而不是只貼客訴原文
3. 每個修復都回到頁面驗證過症狀消失
4. 每個 bug 都補了一個回歸測試，`dotnet test` 全綠
5. 三個獨立 commit，message 說明症狀與根因
6. （思考題）為什麼原本的測試沒抓到這三個 bug？

練習 3

1. `/Products/LowStock` 不帶參數 → 門檻 10 的結果；帶 `?threshold=3` → 結果隨之改變
2. `?threshold=0`、`?threshold=-1` → 頁面顯示驗證錯誤，不是 500
3. 售出數量欄位排除了 Cancelled 訂單（可用一筆已取消的訂單驗證）
4. 停售（已停售 badge）商品不出現在列表
5. 程式分層與命名跟既有的 Products 功能一致（請 agent 自我 review 一次，並自己確認）
6. 至少 3 個新測試，`dotnet test` 全綠

練習 4

1. 重構後 `dotnet test` 全綠
2. 我能說出這次重構「改善了什麼、沒有改變什麼」
3. 我有在 code review 的角度看過 diff（不是 agent 說好就好）

---

## 附錄：值得留下的對話片段

（貼 1–2 段最有代表性的 prompt 與回應**摘要**——不用貼全文，重點是「我怎麼問」和「它怎麼答」。）

- 問「你覺得建單流程是怎麼運作的？」→ agent 給出五步驟描述，其中「折扣只算一次」「取消會還原庫存」兩句話經讀原始碼後證實是錯的，且剛好對應到 `OrderService.CreateOrderAsync` 的 Gold 快照折扣與 `CancelOrderAsync` 的狀態判斷順序錯誤。
