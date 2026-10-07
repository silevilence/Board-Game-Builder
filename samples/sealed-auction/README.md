# 本地密封拍卖

Player 加载本目录，默认 p1 真人、p2／p3 CPU。亦可在 Player 选择热座或同屏，
或在 Editor 打开并配置席位。全 CPU：

```powershell
dotnet run --project src/Player -c Release -- batch samples/sealed-auction --seed 1 --count 20 --out artifacts/auction
```

开始后出价 0～5 并立即锁定，或弃权。全体锁定后，由 p1 截止、揭示、结算。
p1 在未锁定时可推进逻辑收集步；12 步耗尽后未锁定者弃权。这里没有真实时间倒计时。
出价先高者得标，同价按 p1、p2、p3 排序，全体弃权则无人得标。
揭示前任何席位（包括 CPU）的投影只含自己的金额，操作及结果均可回放。
非法出价会被拒绝；CPU 脚本本身故障沿用运行时故障诊断，不假装已完成拍卖。
