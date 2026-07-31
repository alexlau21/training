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
既有測試（`GetOrders_ReportsTotalCountAndTotalPages` 等）只驗證 `TotalCount`/`TotalPages`/狀態篩選，從沒斷言「第幾頁裡面實際是哪幾筆」，所以 Skip 的 off-by-one 完全不會被抓到。
   - Gold 折扣：既有的 `CalculateTotal_AppliesTierDiscountOnSubtotal` 直接手動建構 `Order`/`OrderItem` 並自行設定未打折的 `UnitPriceSnapshot`，繞過了 `CreateOrderAsync`，只測了 `CalculateTotal` 這一個單元，沒有測
「建單 → 計算總額」這條完整路徑，兩段程式碼交互出的雙重折扣就看不到。
   - 庫存還原：既有的 `CancelOrder_ActiveOrder_SetsStatusCancelled` 只斷言 `Status` 變成 `Cancelled`，完全沒檢查取消後的 `StockQuantity`，死碼congenitally 不會被任何測試路徑執行到，自然沒人發現。
   - 共同點：每個既有測試都只驗證單一維度（筆數、折扣率、狀態），沒有測「跨層/跨步驟的最終行為」——這正是這次「先讀完整段程式碼」比「只信任 agent 的文字描述」更可靠的原因。
   
   
練習 3

1. `/Products/LowStock` 不帶參數 → 門檻 10 的結果；帶 `?threshold=3` → 結果隨之改變
2. `?threshold=0`、`?threshold=-1` → 頁面顯示驗證錯誤，不是 500
3. 售出數量欄位排除了 Cancelled 訂單（可用一筆已取消的訂單驗證）
4. 停售（已停售 badge）商品不出現在列表
5. 程式分層與命名跟既有的 Products 功能一致（請 agent 自我 review 一次，並自己確認）
6. 至少 3 個新測試，`dotnet test` 全綠

- 
  「我要新增「低庫存警示頁面」：GET /Products/LowStock?threshold=10，列出 StockQuantity<threshold 且 IsActive 的商品、依庫存升冪排序，欄位要有近 30 天售出數量（排除 Cancelled 訂單），threshold 未帶預設 10、<=0 要顯示驗證錯誤而非 500，庫存 <5 要標記。這功能橫跨 Controller/Service/Repository/ViewModel/View/測試六層，先不要寫程式，動手前先讀 ProductsController、ProductService/IProductService、Views/Products/Index.cshtml，沿用同一套慣例，給我一份實作計畫，我核准後再動手。」
  → 回應先讀完既有三層的實際寫法，才寫計畫：近 30 天銷量用 repository 裡一個 LINQ 關聯子查詢一次查完（不要在迴圈裡逐筆查，避免 N+1）、threshold 驗證沿用 CreateOrderViewModel 同一套 DataAnnotations 機制；等我核准這份計畫後才真的動手寫程式，沒有跳過這一步直接生程式碼。

- 
  「這個功能做完了，另外找一個獨立、完全沒看過這段對話的 agent 去對照規格逐條檢查（結果）；它要自己重新讀程式碼、重新跑一次 build/test，並且實際打 API 驗證行為，不能只讀程式碼就下結論（限制）；有 bug 或漏掉的邊界都要老實講出來，不是來背書的（品質標準）。」
  → 回應另外開了一個沒有這段對話上下文的 agent，逐條核對規格，自己重跑 build/test、用 curl 打了幾個邊界情況（threshold=0、threshold=3、無參數），回報「規格全部符合、沒有發現 bug」，順帶指出兩個非阻塞的小觀察（InMemory 測試無法驗證真正的 SQL 轉譯、數字輸入框少了 `min="1"`）。
  

練習 4

1. 重構後 `dotnet test` 全綠
2. 我能說出這次重構「改善了什麼、沒有改變什麼」
3. 我有在 code review 的角度看過 diff（不是 agent 說好就好）

### 第二階段 — 自建 MCP Server

練習 1

- [o] `dotnet build src/OrderHub.Mcp` 成功
- [o] 獨立 commit（`68be45c` Add OrderHub MCP server with read-only tools）

練習 2

- [o] 三個工具 `customer_orders`/`get_order`/`low_stock` 都列得出來，description、參數說明如所寫
- [o] `low_stock(threshold=10)` 回傳 5 筆商品，和 `/Products/LowStock` 頁面一致
- [o] `get_order` 用不存在 Id（223423）得到清楚錯誤訊息「找不到訂單 223423」，不是 exception dump

過程中撞到一個真實 bug（不是文件裡預告的地雷）：第一次在 Inspector 裡呼叫 `low_stock` 直接回「An error occurred invoking 'low_stock'.」，stderr 顯示 `Microsoft.Data.SqlClient.SqlException...Error Number:2,State:0,Class:20`（連線逾時）。根因是 `OrderHub.Mcp` 沒有自己的 appsettings.json，fallback 連線字串寫死 `Server=localhost`，但本機開發資料庫其實是 `(localdb)\MSSQLLocalDB`（`OrderHub.Web/appsettings.Development.json` 裡設定的）。修正 `Program.cs` 的 fallback 字串（commit `a4b9fa5`）後重測，`low_stock` 正確回傳結果。

練習 3

- [o] `training-repo/.mcp.json` 進 git，獨立 commit（`6460118`）
- [o] Claude Code `/mcp` 可見 `orderhub` 與三個工具

對照實驗——問「哪些商品庫存低於 5?」：

| | 沒有 MCP（手動繞路） | MCP（Inspector CLI） | MCP（Claude Code 原生呼叫） |
|---|---|---|---|
| 呼叫次數 | 4（3 次 grep + 1 次 sqlcmd） | 1 | 1 |
| 需要的先備知識 | domain model 欄位名稱、EF table 命名慣例、實際連線字串（LocalDB vs 誤導性的 localhost fallback） | 無 | 無 |
| 輸出品質 | 類 CSV 文字，中文商品名亂碼（sqlcmd console codepage 問題） | 乾淨 JSON，中文正確 | 乾淨 JSON，中文正確 |
| 正確性風險 | 高（容易漏掉 IsActive 篩選、抓錯 table/欄位、接錯 DB） | 低（規則只在工具裡實作一次） | 低 |
| 實測時間 | 0.654 秒（3 次 grep + 1 次查詢） | 數秒（受 process/build 開銷影響） | 幾乎即時（server 已連線，單一 round-trip） |
| 穩定性 | 每次都可重現（前提是已經知道怎麼查） | 一開始不穩：遇到 `dotnet run` 重建鎖檔導致的重連失敗（2 次 `-32000`） | 修正成指向 publish 後的執行檔（commit `8696a38`）後穩定 |
| 團隊可重用性 | 沒人共用這段繞路，每個人都要重新摸索一次 | 需要手動開 Inspector | `.mcp.json` 進 git，任何人開這個 repo 用 Claude Code 就自動接上 |

結論：速度差異（0.654 秒 vs 幾乎即時）其實不是重點——手動查詢會這麼快，是因為這次除錯過程裡我已經先知道 schema 和 DB 位置；真正冷啟動一定慢得多。真正的價值在於消除猜測、消除編碼問題，以及把「庫存 < 門檻 且上架中，依庫存升冪排序」這條業務規則收斂到工具裡實作一次，而不是每個人每次手動查詢時各自重新兜一次、還可能兜錯。

意外插曲：MCP server 用 `dotnet run` 當啟動指令時，每次重新連線都會觸發整個專案重新編譯；如果前一個 server process 還沒完全結束（例如瀏覽器分頁裡開著的 MCP Inspector 沒關），重編譯的檔案複製步驟就會撞到檔案鎖（`MSB3027`），導致新啟動的 process 在完成 MCP handshake 前就先掛掉，Claude Code 端看到的就是 `-32000`。改成 `.mcp.json` 直接指向 `dotnet publish` 產出的執行檔（不再每次連線都重編譯）後，這個問題不再出現。代價：每次改了 `OrderHub.Mcp` 的程式碼，或是別人第一次 clone 這個 repo，都要手動跑一次 `dotnet publish src/OrderHub.Mcp -c Release -o src/OrderHub.Mcp/publish`——這個步驟目前只寫在 commit message 裡，還沒補進 README。

練習 4

- [o] MCP Inspector 中 `cancel_order` 的 annotations：`destructiveHint: true`、`idempotentHint: false`；三個唯讀工具都顯示 `readOnlyHint: true`
- [o] 對一筆待處理訂單（#2006，SKU-1001 × 2）呼叫 `cancel_order`：回傳「訂單 2006 已取消,庫存已回補」，直接查 DB 確認 SKU-1001 庫存從 23 變回 25
- [o] 對同一筆訂單再取消一次：回傳「取消失敗:狀態為 Cancelled 的訂單不可取消」，是清楚的拒絕訊息而非 exception dump
- [ ] 對 agent 說「幫我取消訂單 X」，親眼看到 Claude Code 的權限確認提示——這次驗證是透過 MCP Inspector CLI 直接呼叫工具做的，Inspector 不會像 Claude Code 一樣依 annotations 跳確認，所以這一項還沒有真的驗證過，之後要在 Claude Code 裡對真人 agent 對話重跑一次

---

## 附錄：值得留下的對話片段

（貼 1–2 段最有代表性的 prompt 與回應**摘要**——不用貼全文，重點是「我怎麼問」和「它怎麼答」。）

- 問「你覺得建單流程是怎麼運作的？」→ agent 給出五步驟描述，其中「折扣只算一次」「取消會還原庫存」兩句話經讀原始碼後證實是錯的，且剛好對應到 `OrderService.CreateOrderAsync` 的 Gold 快照折扣與 `CancelOrderAsync` 的狀態判斷順序錯誤。

- 
  「商品頁庫存數字跟實際盤點兜不起來，而且好像每次取消訂單之後庫存就變得更少。我實測過：SKU-1001 原本庫存 27，建一筆數量 1 的訂單後正確變成 26；但把這筆訂單取消之後，庫存還是 26，沒有變回 27。我懷疑問題出在 CancelOrderAsync 判斷可取消狀態那段邏輯，麻煩先讀完整份 OrderService.cs 找根因，先不要動手改，找到後跟我說原因。」
  → 回應讀完 `CancelOrderAsync` 全文後指出：`order.Status = OrderStatus.Cancelled` 這行寫在還原庫存的判斷式 `if (order.Status == Pending || Confirmed)` 之前，所以那個判斷式讀到的其實是「已經被改過」的狀態，永遠不成立，庫存還原的程式碼形同虛設——先回報根因，等確認後才動手修。

- 
  「財務對帳說 Gold 會員的訂單金額比手算少一截，但 Silver 完全正常。麻煩先補一個會重現這個症狀的回歸測試（要真的呼叫 CreateOrderAsync 走一次完整建單流程，不要手動建構 Order 物件繞過去），跑給我看它真的失敗，再去改 OrderService 的原始碼。」
  → 回應先寫了 `CreateOrder_GoldCustomer_TotalDiscountedOnlyOnce`，跑起來後確認真的失敗（`UnitPriceSnapshot` 變成 900 而非 1000，證實 Gold 會員在建單當下就被多打了一次折），才動手拿掉 `CreateOrderAsync` 裡針對 Gold 的預先折扣區塊，改完重跑測試轉綠。



