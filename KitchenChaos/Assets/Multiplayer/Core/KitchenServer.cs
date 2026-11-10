using System;
using System.Collections.Generic;
using System.Linq;

namespace KitchenChaos.Multiplayer {
    // All methods execute on the owning Unity main thread. No coroutine/await inside a command.
    // This class has no dependency on Unity and is exercised by the console regression suite.
    public sealed class KitchenServer {
        public readonly Rules Rules;
        public readonly List<PlayerState> Players = new List<PlayerState>();
        public readonly List<StationState> Stations;
        public readonly List<ItemState> Items = new List<ItemState>();
        public readonly List<Order> Orders = new List<Order>();
        public Phase Phase { get; private set; }
        public int Epoch { get; private set; }
        public int Host { get; private set; }
        public string Code { get; private set; }
        public Recipe[] Menu { get; private set; } = new Recipe[0];
        public Summary Result { get; private set; } = new Summary();
        public float Remaining { get; private set; }
        public float Countdown { get; private set; }
        // Return corrected legal position after static-geometry collision resolution.
        public Func<PlayerState, float, float, float[]> ResolveMove;
        private readonly Random rng;
        private readonly Dictionary<int, Ledger> ledgers = new Dictionary<int, Ledger>();
        private readonly HashSet<int> participants = new HashSet<int>();
        private int nextItem = 1, nextOrder = 1;
        private float orderTimer;
        private long revision, eventId;
        private string lastEvent;
        private int lastActor, lastStation, lastReward;
        private sealed class Entry { public string fingerprint; public Packet result; }
        private sealed class Ledger {
            public long highest;
            public readonly Dictionary<long, Entry> cache = new Dictionary<long, Entry>();
            public readonly Queue<long> order = new Queue<long>();
        }
        public KitchenServer(Rules rules, IEnumerable<StationState> stations, string roomCode, int seed = 0) {
            rules.Validate(); Rules = rules; Stations = stations.ToList(); Code = roomCode;
            rng = seed == 0 ? new Random() : new Random(seed);
            if (Stations.Select(s => s.id).Distinct().Count() != Stations.Count) throw new ArgumentException("重复设备编号");
        }
        public PlayerState AddPlayer(int id, string name, float x, float z) {
            if (Phase != Phase.Lobby || Players.Count >= Rules.maxPlayers || Players.Any(p => p.id == id)) return null;
            int slot=Enumerable.Range(0,Rules.maxPlayers).First(v=>!Players.Any(p=>p.slot==v));
            var player = new PlayerState { id = id, slot=slot, name = CleanName(name), x = x, z = z, spawnX=x,spawnZ=z };
            Players.Add(player); ledgers.Add(id, new Ledger());
            if (Host == 0) Host = id;
            ResetReady(); Emit("join", id, 0); return player;
        }
        public static string CleanName(string s) {
            s = string.IsNullOrWhiteSpace(s) ? "厨师" : s.Trim();
            s = new string(s.Where(c => !char.IsControl(c) && c != '<' && c != '>').ToArray());
            return s.Length > 16 ? s.Substring(0, 16) : s;
        }
        public void RemovePlayer(int id) {
            var p = Players.Find(v => v.id == id); if (p == null) return;
            var item = FindItem(p.hand);
            if (item != null) {
                var free = Stations.Where(s => s.kind == StationKind.Clear && s.item == 0)
                    .OrderBy(s => Dist2(s.x, s.z, p.x, p.z)).FirstOrDefault();
                if (free != null) { free.item = item.id; free.version++; item.owner = "c:" + free.id; }
                else Items.Remove(item); // Documented fallback when every safe worktop is occupied.
            }
            Players.Remove(p); ledgers.Remove(id);
            if (Phase == Phase.Lobby) ResetReady();
            Emit("leave", id, 0);
        }
        private void ResetReady() { foreach (var p in Players) p.ready = false; }
        public void SetInput(int id, float x, float z, bool run) {
            var p = Players.Find(v => v.id == id);
            if (p == null || float.IsNaN(x) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(z)) return;
            float length = (float)Math.Sqrt(x*x + z*z);
            if (length > 1) { x /= length; z /= length; }
            p.inputX = x; p.inputZ = z; p.inputRun = run; p.inputAge = 0;
        }
        public Packet Command(int playerId, Packet request) {
            Ledger ledger;
            if (request == null || !ledgers.TryGetValue(playerId, out ledger)) return Ack(request, false, "连接身份无效");
            string fingerprint = request.epoch + "|" + request.action + "|" + request.target + "|" + request.expectedVersion + "|" + request.ready;
            Entry existing;
            if (ledger.cache.TryGetValue(request.seq, out existing))
                return existing.fingerprint == fingerprint ? existing.result : Ack(request, false, "请求编号被用于不同操作");
            if (request.seq <= ledger.highest || request.seq <= 0) return Ack(request, false, "请求编号过期");
            ledger.highest = request.seq;
            Packet response;
            if (request.epoch != Epoch) response = Ack(request, false, "对局已变更，请等待最新状态");
            else response = Execute(playerId, request);
            ledger.cache.Add(request.seq, new Entry { fingerprint = fingerprint, result = response });
            ledger.order.Enqueue(request.seq);
            while (ledger.order.Count > 256) ledger.cache.Remove(ledger.order.Dequeue());
            return response;
        }
        private static Packet Ack(Packet r, bool ok, string reason) {
            return new Packet { type = "ack", seq = r == null ? 0 : r.seq, ok = ok, reason = reason, action = r == null ? "" : r.action };
        }
        private Packet Execute(int id, Packet r) {
            var p = Players.Find(v => v.id == id);
            if (r.action == "ready") {
                if (Phase != Phase.Lobby) return Ack(r, false, "当前不在大厅");
                p.ready = r.ready; return Ack(r, true, p.ready ? "已准备" : "已取消准备");
            }
            if (r.action == "start") {
                if (id != Host || Phase != Phase.Lobby || Players.Count < Rules.minPlayers || Players.Any(v => !v.ready))
                    return Ack(r, false, "至少两名玩家且全员准备后，由房主开始");
                Begin(); return Ack(r, true, "正在开局");
            }
            if (r.action == "return") {
                if (id != Host || Phase != Phase.Results) return Ack(r, false, "结算后由房主返回房间");
                Phase = Phase.Lobby; ClearWorld(); ResetReady(); Emit("lobby", id, 0);
                return Ack(r, true, "已返回房间");
            }
            if (Phase != Phase.Playing) return Ack(r, false, "对局尚未开始或已经结束");
            var s = Stations.Find(v => v.id == r.target);
            if (s == null || Dist2(p.x,p.z,s.x,s.z) > Rules.interactDistance*Rules.interactDistance)
                return Ack(r, false, "请靠近操作台");
            if (s.version != r.expectedVersion) return Ack(r, false, "设备状态已被队友更新，请重新操作");
            string reason;
            bool success = r.action == "cut" ? Cut(p,s,out reason) :
                r.action == "interact" ? Interact(p,s,out reason) : Reject(out reason,"未知操作");
            return Ack(r, success, reason);
        }
        private static bool Reject(out string reason, string text) { reason = text; return false; }
        private void ClearWorld() {
            Items.Clear(); Orders.Clear(); Menu = new Recipe[0];
            foreach (var p in Players) { p.hand = 0; p.inputX = p.inputZ = 0; p.walking = p.running = false; }
            foreach (var s in Stations) { s.item = s.cuts = 0; s.elapsed = s.progress = s.plateTimer = 0; s.plateStock = 4; s.version++; }
        }
        private void Begin() {
            ClearWorld(); Epoch++; Result = new Summary(); participants.Clear();
            foreach (var p in Players) { participants.Add(p.id);p.x=p.spawnX;p.z=p.spawnZ; }
            var choices = Rules.recipes.ToList();
            for (int i = choices.Count-1; i > 0; i--) { int j = rng.Next(i+1); var t = choices[i]; choices[i] = choices[j]; choices[j] = t; }
            Menu = choices.Take(4).ToArray(); Remaining = Rules.roundSeconds; Countdown = Rules.countdownSeconds;
            Phase = Phase.Countdown; orderTimer = Rules.orderInterval; Emit("start", Host, 0);
        }
        public void Step(float dt) {
            if (float.IsNaN(dt) || dt <= 0) return;
            // Small fixed steps keep collision checks and all timer boundaries deterministic.
            float left = dt;
            while (left > .00001f) { float part = Math.Min(left,.05f); Tick(part); left -= part; }
        }
        private void Tick(float dt) {
            if (Phase == Phase.Countdown) {
                Countdown = Math.Max(0,Countdown-dt);
                if (Countdown <= .0001f) { Phase = Phase.Playing; for (int i=0;i<Rules.maxOrders;i++) SpawnOrder(i); }
                return;
            }
            if (Phase != Phase.Playing) return;
            float active = Math.Min(dt, Remaining);
            foreach (var p in Players) {
                p.inputAge += dt;
                float x = p.inputAge > .3f ? 0 : p.inputX, z = p.inputAge > .3f ? 0 : p.inputZ;
                p.walking = x*x+z*z > .001f; p.running = p.walking && p.inputRun;
                if (p.walking) {
                    p.facingX = x; p.facingZ = z;
                    float scale = (p.running ? Rules.runSpeed : Rules.walkSpeed)*active;
                    float nx = p.x+x*scale, nz = p.z+z*scale;
                    if (ResolveMove != null) { var pos = ResolveMove(p,nx,nz); nx = pos[0]; nz = pos[1]; }
                    if (!Players.Any(other => other.id != p.id && Dist2(nx,nz,other.x,other.z) < .81f)) { p.x=nx; p.z=nz; }
                }
            }
            foreach (var s in Stations) {
                if (s.kind == StationKind.Plates) {
                    s.plateTimer += active;
                    if (s.plateTimer >= Rules.plateInterval) { s.plateTimer -= Rules.plateInterval; if (s.plateStock < 4) { s.plateStock++; s.version++; } }
                }
                var item = FindItem(s.item);
                if (s.kind != StationKind.Stove || item == null) continue;
                if (item.kind != "MeatPattyUncooked" && item.kind != "MeatPattyCooked") continue;
                s.elapsed += active;
                float duration = item.kind == "MeatPattyUncooked" ? Rules.frySeconds : Rules.burnSeconds;
                s.progress = Math.Min(1,s.elapsed/duration);
                if (s.elapsed >= duration-.0001f) {
                    item.kind = item.kind == "MeatPattyUncooked" ? "MeatPattyCooked" : "MeatPattyBurned";
                    s.elapsed = s.progress = 0; s.version++; Emit(item.kind == "MeatPattyCooked" ? "cooked" : "burned",0,s.id);
                }
            }
            Remaining = Math.Max(0,Remaining-active); orderTimer -= active;
            if (Remaining <= .0001f) { Finish(); return; }
            if (orderTimer <= .0001f) { orderTimer = Rules.orderInterval; if (Orders.Count < Rules.maxOrders) SpawnOrder(); }
        }
        private void SpawnOrder(int recipe=-1) { Orders.Add(new Order { id = nextOrder++, recipe = recipe<0?rng.Next(Menu.Length):recipe, createdAt = Rules.roundSeconds-Remaining }); }
        private void Finish() {
            Phase = Phase.Results; Remaining = 0; Result.expired = Orders.Count;
            UpdateIncome();
            Result.stars = 1; foreach (var t in Rules.starThresholds) if (Result.score >= t) Result.stars++;
            Result.participants = participants.Count;
            Result.accuracy = Result.completed+Result.wrong == 0 ? 0 : (float)Result.completed/(Result.completed+Result.wrong);
            foreach (var p in Players) p.walking = p.running = false;
            Emit("finish",0,0);
        }
        private void UpdateIncome() {
            Result.score = Math.Max(0,Result.earned-Result.wrong*Rules.wrongPenalty-Result.expired*Rules.timeoutPenalty);
        }
        public ItemState FindItem(int id) { return id == 0 ? null : Items.Find(i => i.id == id); }
        private void Give(PlayerState p, string kind) {
            var item = new ItemState { id = nextItem++, kind = kind, owner = "p:"+p.id }; Items.Add(item); p.hand = item.id;
        }
        private bool Interact(PlayerState p, StationState s, out string reason) {
            var hand = FindItem(p.hand); var top = FindItem(s.item); reason = "操作成功";
            if (s.kind == StationKind.Supply) {
                if (hand != null) return Reject(out reason,"请先放下手中物品");
                Give(p,s.supply); s.version++; Emit("pickup",p.id,s.id); return true;
            }
            if (s.kind == StationKind.Plates) {
                if (hand != null || s.plateStock <= 0) return Reject(out reason,"需要空手且餐盘架上有盘子");
                Give(p,"Plate"); s.plateStock--; s.version++; Emit("pickup",p.id,s.id); return true;
            }
            if (s.kind == StationKind.Trash) {
                if (hand == null) return Reject(out reason,"手中没有需要丢弃的物品");
                Items.Remove(hand); p.hand=0; s.version++; Emit("trash",p.id,s.id); return true;
            }
            if (s.kind == StationKind.Delivery) {
                if (hand == null || hand.kind != "Plate") return Reject(out reason,"请端着餐盘出餐");
                var order = Orders.Find(o => Menu[o.recipe].ingredients.SequenceEqual(hand.ingredients));
                if (order == null) { Result.wrong++; Emit("wrong",p.id,s.id); reason="菜品与订单不符，扣款并回收餐盘"; }
                else {
                    Orders.Remove(order); Result.completed++;
                    int reward=Menu[order.recipe].reward; Result.earned+=reward;
                    float seconds = Rules.roundSeconds-Remaining-order.createdAt;
                    if (Result.completed == 1 || seconds < Result.bestSeconds) Result.bestSeconds = seconds;
                    Emit("delivery",p.id,s.id,reward);
                }
                UpdateIncome();
                Items.Remove(hand); p.hand=0; s.version++; return true;
            }
            if (hand != null && top != null) {
                ItemState plate = hand.kind == "Plate" ? hand : top.kind == "Plate" ? top : null;
                ItemState ingredient = plate == hand ? top : hand;
                if (plate == null || !IsPlatable(ingredient.kind) || plate.ingredients.Length >= 8)
                    return Reject(out reason,"台面已占用，或食材尚未加工完成");
                plate.ingredients = plate.ingredients.Concat(new[]{ingredient.kind}).ToArray();
                if (ingredient == hand) p.hand=0; else { s.item=0; s.cuts=0; s.elapsed=s.progress=0; }
                Items.Remove(ingredient); s.version++; Emit("plate",p.id,s.id); return true;
            }
            if (hand == null && top != null) {
                p.hand=top.id; top.owner="p:"+p.id; s.item=0; s.cuts=0; s.progress=s.elapsed=0;
                s.version++; Emit("pickup",p.id,s.id); return true;
            }
            if (hand != null && top == null) {
                if (s.kind == StationKind.Cutting && CutOutput(hand.kind) == null) return Reject(out reason,"砧板只接受未切配的蔬菜或奶酪");
                if (s.kind == StationKind.Stove && hand.kind != "MeatPattyUncooked" && hand.kind != "MeatPattyCooked") return Reject(out reason,"炉灶只接受肉饼");
                s.item=hand.id; hand.owner="c:"+s.id; p.hand=0; s.cuts=0; s.progress=s.elapsed=0;
                s.version++; Emit("drop",p.id,s.id); return true;
            }
            return Reject(out reason,"没有可拿取的物品");
        }
        private bool Cut(PlayerState p, StationState s, out string reason) {
            var top=FindItem(s.item);
            if (s.kind != StationKind.Cutting || top == null || CutOutput(top.kind) == null || p.hand != 0)
                return Reject(out reason,"请空手在有待切食材的砧板前操作");
            // Intermediate cuts update the snapshot, but do not replace the ingredient or its station revision.
            // Multiple key presses can therefore target the same ingredient before a new snapshot arrives.
            s.cuts++; s.progress=(float)s.cuts/Rules.cutCount;
            if (s.cuts >= Rules.cutCount) { top.kind=CutOutput(top.kind); s.progress=1; s.version++; }
            Emit("cut",p.id,s.id); reason="切配成功"; return true;
        }
        public static string CutOutput(string kind) {
            if (kind == "Tomato") return "TomatoSlices";
            if (kind == "Cabbage") return "CabbageSlices";
            if (kind == "CheeseBlock") return "CheeseSlices";
            return null;
        }
        public static bool IsPlatable(string kind) {
            return kind == "Bread" || kind == "TomatoSlices" || kind == "CabbageSlices" || kind == "CheeseSlices" || kind == "MeatPattyCooked";
        }
        private static float Dist2(float x,float z,float a,float b) { return (x-a)*(x-a)+(z-b)*(z-b); }
        private void Emit(string type,int actor,int station,int reward=0) { eventId++; lastEvent=type; lastActor=actor; lastStation=station; lastReward=reward; }
        // Caller serializes immediately on the same thread; clients only ever receive detached JSON snapshots.
        public Snapshot GetSnapshot() {
            return new Snapshot { revision=++revision, epoch=Epoch, host=Host, code=Code, phase=Phase,
                remaining=Remaining,countdown=Countdown,players=Players.ToArray(),stations=Stations.ToArray(),items=Items.ToArray(),
                menu=Menu,orders=Orders.ToArray(),summary=Result,eventId=eventId,lastEvent=lastEvent,lastActor=lastActor,lastStation=lastStation,lastReward=lastReward };
        }
        public void CheckInvariants() {
            var references = Players.Where(p=>p.hand!=0).Select(p=>p.hand).Concat(Stations.Where(s=>s.item!=0).Select(s=>s.item)).ToArray();
            if (references.Distinct().Count()!=references.Length || references.Length!=Items.Count) throw new InvalidOperationException("物品引用不唯一");
            foreach (var p in Players) if (p.hand!=0 && (FindItem(p.hand)==null || FindItem(p.hand).owner!="p:"+p.id)) throw new InvalidOperationException("玩家归属错误");
            foreach (var s in Stations) if (s.item!=0 && (FindItem(s.item)==null || FindItem(s.item).owner!="c:"+s.id)) throw new InvalidOperationException("台面归属错误");
        }
    }
}
