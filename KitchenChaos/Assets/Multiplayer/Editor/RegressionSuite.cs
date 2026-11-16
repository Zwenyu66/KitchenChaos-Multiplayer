using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using kcp2k;
using System.Text;
using System.Threading;

namespace KitchenChaos.Multiplayer {
    public static class RegressionSuite {
        private static int count;
        private static Action<string> output;
        private static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
        private static void Test(string name,Action body) { body();count++;output("PASS "+name); }
        private sealed class Fixture {
            public KitchenServer game;
            private readonly Dictionary<int,long> sequence=new Dictionary<int,long>{{1,0},{2,0},{3,0},{4,0}};
            public Fixture(bool begin=true) {
                var rules=new Rules{recipes=new[]{
                    new Recipe{name="汉堡",reward=100,ingredients=new[]{"Bread","MeatPattyCooked"}},
                    new Recipe{name="沙拉",reward=80,ingredients=new[]{"CabbageSlices","TomatoSlices"}},
                    new Recipe{name="双番茄",reward=80,ingredients=new[]{"TomatoSlices","TomatoSlices"}},
                    new Recipe{name="双奶酪",reward=95,ingredients=new[]{"Bread","CheeseSlices","CheeseSlices"}}
                }};
                var stations=new List<StationState>();int i=0;
                foreach(var ingredient in new[]{"Bread","Tomato","Cabbage","CheeseBlock","MeatPattyUncooked"})
                    stations.Add(new StationState{id=++i,kind=StationKind.Supply,supply=ingredient,x=i*5});
                stations.Add(new StationState{id=10,kind=StationKind.Clear,x=50});
                stations.Add(new StationState{id=11,kind=StationKind.Cutting,x=55});
                stations.Add(new StationState{id=12,kind=StationKind.Stove,x=60});
                stations.Add(new StationState{id=13,kind=StationKind.Plates,x=65});
                stations.Add(new StationState{id=14,kind=StationKind.Delivery,x=70});
                stations.Add(new StationState{id=15,kind=StationKind.Trash,x=75});
                game=new KitchenServer(rules,stations,"ABC123",123);
                game.AddPlayer(1,"房主",0,0);game.AddPlayer(2,"队友",0,3);
                if(begin) Start();
            }
            public Packet Cmd(int player,string action,int target=0,bool ready=false) {
                var station=game.Stations.Find(s=>s.id==target);
                return new Packet{seq=++sequence[player],epoch=game.Epoch,action=action,target=target,expectedVersion=station==null?0:station.version,ready=ready};
            }
            public Packet Do(int player,string action,int target=0,bool ready=false) { return game.Command(player,Cmd(player,action,target,ready)); }
            public void Start() { Do(1,"ready",ready:true);Do(2,"ready",ready:true);Check(Do(1,"start").ok,"start");game.Step(.05f+game.Rules.countdownSeconds); }
            public PlayerState Player(int id) { return game.Players.Find(p=>p.id==id); }
            // Pure rule tests position the fixture directly; actual Unity collision and UI require the manual matrix.
            public void At(int id,int station) { var s=game.Stations.Find(v=>v.id==station);Player(id).x=s.x;Player(id).z=s.z+1; }
            public void Interact(int id,int station) { At(id,station);Check(Do(id,"interact",station).ok,"interact "+station);game.CheckInvariants(); }
            public void Ingredient(int player,string ingredient) {
                string raw=ingredient=="TomatoSlices"?"Tomato":ingredient=="CabbageSlices"?"Cabbage":ingredient=="CheeseSlices"?"CheeseBlock":ingredient=="MeatPattyCooked"?"MeatPattyUncooked":ingredient;
                int basket=game.Stations.First(s=>s.kind==StationKind.Supply&&s.supply==raw).id;
                Interact(player,basket);
                if(KitchenServer.CutOutput(raw)!=null) {
                    Interact(player,11);
                    for(int i=0;i<game.Rules.cutCount;i++) Check(Do(player,"cut",11).ok,"cut");
                    Interact(player,11);
                } else if(raw=="MeatPattyUncooked") { Interact(player,12);game.Step(game.Rules.frySeconds);Interact(player,12); }
                Check(game.FindItem(Player(player).hand).kind==ingredient,"processed ingredient");
            }
            public void Plate(int player,string[] ingredients) {
                Interact(player,13);Interact(player,10);
                foreach(var ingredient in ingredients) { Ingredient(player,ingredient);Interact(player,10); }
                Interact(player,10);
            }
        }
        public static int Run(Action<string> log) {
            count=0;output=log;
            Test("Lobby readiness host authority and membership changes",()=>{
                var f=new Fixture(false);Check(!f.Do(1,"start").ok,"unready start");
                f.Do(1,"ready",ready:true);f.Do(2,"ready",ready:true);Check(!f.Do(2,"start").ok,"client start");
                Check(f.game.AddPlayer(3,"third",5,0)!=null,"join");Check(f.game.Players.All(p=>!p.ready),"ready reset");
                f.game.AddPlayer(4,"fourth",8,0);Check(f.game.AddPlayer(5,"fifth",10,0)==null,"room cap");
                f.game.RemovePlayer(3);Check(f.game.Players.Count==3,"leave");
            });
            Test("Four shared recipes and four initial orders",()=>{
                var f=new Fixture();Check(f.game.Menu.Length==4&&f.game.Orders.Count==4,"initial orders");
                Check(f.game.Orders.Select(o=>o.recipe).Distinct().Count()==4,"initial recipe coverage");
                Check(f.game.AddPlayer(3,"late",0,0)==null,"midmatch join");
            });
            Test("Server rejects distant inactive unknown and stale requests",()=>{
                var f=new Fixture();Check(!f.Do(1,"interact",1).ok,"distance");
                f.At(1,1);var stale=f.Cmd(1,"interact",1);stale.expectedVersion--;Check(!f.game.Command(1,stale).ok,"version");
                Check(!f.Do(1,"interact",999).ok,"unknown station");
                Check(!f.game.Command(999,f.Cmd(1,"interact",1)).ok,"identity");
                f.game.Step(301);Check(!f.Do(1,"interact",1).ok,"after finish");
            });
            Test("Only one player can pick up a contested item",()=>{
                var f=new Fixture();f.Interact(1,1);f.Interact(1,10);f.At(1,10);f.At(2,10);
                var a=f.Cmd(1,"interact",10);var b=f.Cmd(2,"interact",10);
                Check(f.game.Command(1,a).ok,"first claim");Check(!f.game.Command(2,b).ok,"second claim");
                Check(f.Player(1).hand!=0&&f.Player(2).hand==0,"single owner");f.game.CheckInvariants();
            });
            Test("Duplicate results replay and changed payload is rejected",()=>{
                var f=new Fixture();f.At(1,1);var request=f.Cmd(1,"interact",1);
                Check(f.game.Command(1,request).ok,"first execution");int item=f.Player(1).hand;
                Check(f.game.Command(1,request).ok,"replay");Check(f.game.Items.Count==1&&f.Player(1).hand==item,"no duplicate spawn");
                request.action="cut";Check(!f.game.Command(1,request).ok,"payload reuse");
            });
            Test("Evicted sequence numbers cannot execute again",()=>{
                var f=new Fixture();f.At(1,1);var request=f.Cmd(1,"interact",1);f.game.Command(1,request);
                for(int i=0;i<300;i++) f.Do(1,"unknown");
                Check(!f.game.Command(1,request).ok,"ancient replay");f.game.CheckInvariants();
            });
            Test("Rapid cuts need no wait or newer snapshot and duplicate packets count once",()=>{
                var f=new Fixture();f.Interact(1,2);f.Interact(1,11);
                var board=f.game.Stations.Find(s=>s.id==11);
                var commands=Enumerable.Range(0,f.game.Rules.cutCount).Select(i=>f.Cmd(1,"cut",11)).ToArray();
                float remaining=f.game.Remaining;
                Check(f.game.Command(1,commands[0]).ok,"first cut");
                Check(f.game.Command(1,commands[0]).ok&&board.cuts==1,"replayed cut counts once");
                for(int i=1;i<commands.Length;i++) Check(f.game.Command(1,commands[i]).ok,"queued rapid cut");
                Check(f.game.Remaining==remaining,"no elapsed time needed");
                Check(f.game.FindItem(board.item).kind=="TomatoSlices"&&board.progress==1,"slices");
                Check(!f.Do(1,"cut",11).ok,"finished ingredient cannot be cut again");
            });
            Test("A delayed cut cannot affect a replacement ingredient",()=>{
                var f=new Fixture();f.Interact(1,2);f.Interact(1,11);f.At(2,11);
                var delayed=f.Cmd(2,"cut",11);
                f.Interact(1,11);f.Interact(1,15);f.Interact(1,3);f.Interact(1,11);
                Check(!f.game.Command(2,delayed).ok,"stale cut rejected");
                var board=f.game.Stations.Find(s=>s.id==11);
                Check(board.cuts==0&&f.game.FindItem(board.item).kind=="Cabbage","replacement stays intact");
            });
            Test("Cooking and burning are driven by server time",()=>{
                var f=new Fixture();f.Interact(1,5);f.Interact(1,12);f.game.Step(8);
                var stove=f.game.Stations.Find(s=>s.id==12);Check(f.game.FindItem(stove.item).kind=="MeatPattyCooked","cooked");
                f.game.Step(8);Check(f.game.FindItem(stove.item).kind=="MeatPattyBurned","burned");f.game.CheckInvariants();
            });
            Test("Plate accepts repeated ingredients in exact quantities",()=>{
                var f=new Fixture();var order=f.game.Orders.First(o=>f.game.Menu[o.recipe].name=="双番茄");
                f.Plate(1,f.game.Menu[order.recipe].ingredients);Check(f.game.FindItem(f.Player(1).hand).ingredients.Length==2,"duplicate ingredient quantity");
                f.Interact(1,14);Check(f.game.Result.completed==1&&f.game.Items.Count==0,"correct repeated dish");
            });
            Test("Complete take cut cook plate deliver loop",()=>{
                var f=new Fixture();var order=f.game.Orders.First(o=>f.game.Menu[o.recipe].name=="汉堡");
                f.Plate(1,f.game.Menu[order.recipe].ingredients);f.Interact(1,14);
                Check(f.game.Result.completed==1&&f.game.Result.score==100,"success score");
                Check(f.game.Result.bestSeconds>=8,"best delivery time");Check(f.game.Orders.Count==3,"order consumed");
                f.game.Step(4);Check(f.game.Orders.Count==4,"four second refill");
            });
            Test("Reversed assembly order succeeds and duplicate delivery pays once",()=>{
                var f=new Fixture();f.Plate(1,new[]{"MeatPattyCooked","Bread"});f.At(1,14);
                var r=f.Cmd(1,"interact",14);Check(f.game.Command(1,r).ok,"wrong delivery is processed");
                f.game.Command(1,r);Check(f.game.Result.completed==1&&f.game.Result.wrong==0&&f.game.Result.earned==100&&f.Player(1).hand==0,"reordered ingredients and replay");
            });
            Test("Recipe-specific payments persist through deductions and final settlement",()=>{
                var f=new Fixture();
                f.Plate(1,new[]{"CabbageSlices","TomatoSlices"});f.Interact(1,14);
                Check(f.game.GetSnapshot().lastReward==80&&f.game.Result.score==80,"salad pays 80");
                f.Plate(1,new[]{"Bread","MeatPattyCooked"});f.At(1,14);
                var delivery=f.Cmd(1,"interact",14);
                Check(f.game.Command(1,delivery).ok&&f.game.Command(1,delivery).ok,"delivery and replay");
                Check(f.game.GetSnapshot().lastReward==100&&f.game.Result.earned==180&&f.game.Result.score==180,"burger pays 100 once");
                f.Interact(1,13);f.Interact(1,14);
                Check(f.game.Result.score==170,"wrong delivery deducts 10");
                f.game.Step(8); // The two successful deliveries free two order slots; both refill before closing.
                Check(f.game.Orders.Count==4,"outstanding orders refilled");
                f.game.Step(301);
                Check(f.game.Phase==Phase.Results&&f.game.Result.expired==4&&f.game.Result.earned==180&&f.game.Result.score==130,
                    "settlement keeps individual rewards and deducts the four orders remaining at closing");
            });
            Test("Raw and burned food cannot be plated",()=>{
                var f=new Fixture();f.Interact(1,13);f.Interact(1,10);f.Interact(1,2);f.At(1,10);
                Check(!f.Do(1,"interact",10).ok,"raw tomato");f.Interact(1,15);f.Interact(1,5);f.Interact(1,12);f.game.Step(16);f.Interact(1,12);f.At(1,10);
                Check(!f.Do(1,"interact",10).ok,"burned meat");f.game.CheckInvariants();
            });
            Test("Occupied hands stations and plate stock remain valid",()=>{
                var f=new Fixture();f.Interact(1,1);f.At(1,2);Check(!f.Do(1,"interact",2).ok,"occupied hands");
                f.Interact(1,10);f.Interact(2,1);f.At(2,10);Check(!f.Do(2,"interact",10).ok,"occupied top");
                f.Interact(2,15);f.Interact(2,13);f.Interact(2,15);
                Check(f.game.Stations.Find(s=>s.id==13).plateStock==3,"stock take");f.game.Step(4);
                Check(f.game.Stations.Find(s=>s.id==13).plateStock==4,"stock refill");f.game.CheckInvariants();
            });
            Test("Disconnect moves held item to a free worktop",()=>{
                var f=new Fixture();f.Interact(2,2);int item=f.Player(2).hand;f.game.RemovePlayer(2);
                Check(f.game.FindItem(item).owner=="c:10","recovered");f.game.CheckInvariants();
                Check(f.game.Phase==Phase.Playing,"remaining player continues");
            });
            Test("Disconnect clears held item when every safe top is full",()=>{
                var f=new Fixture();f.Interact(1,1);f.Interact(1,10);f.Interact(2,2);int item=f.Player(2).hand;
                f.game.RemovePlayer(2);Check(f.game.FindItem(item)==null,"cleanup fallback");f.game.CheckInvariants();
            });
            Test("Global end produces frozen shared score and summary",()=>{
                var f=new Fixture();f.game.Step(301);Check(f.game.Phase==Phase.Results,"finish");
                Check(f.game.Result.expired==4&&f.game.Result.score==0&&f.game.Result.stars==1,"end score");
                Check(f.game.Result.participants==2&&f.game.Remaining==0,"participants");
                var old=f.game.GetSnapshot();f.game.Step(20);Check(f.game.Result.score==old.summary.score,"freeze");
            });
            Test("Rematch increments epoch and rejects delayed old commands",()=>{
                var f=new Fixture();int epoch=f.game.Epoch;f.game.Step(301);Check(f.Do(1,"return").ok,"return");
                Check(f.game.Items.Count==0&&f.game.Players.All(p=>!p.ready),"reset");f.Start();
                Check(f.game.Epoch==epoch+1,"new epoch");f.At(1,1);var request=f.Cmd(1,"interact",1);request.epoch=epoch;
                Check(!f.game.Command(1,request).ok,"old match command");f.game.CheckInvariants();
            });
            Test("Movement normalizes input expires and rejects nonfinite values",()=>{
                var f=new Fixture();f.Player(2).x=100;f.game.SetInput(1,1000,0,false);f.game.Step(.1f);
                Check(f.Player(1).x<=.701f&&f.Player(1).x>.6f,"authority speed cap");
                f.game.Step(1);float x=f.Player(1).x;f.game.Step(1);Check(f.Player(1).x==x,"input timeout");
                f.game.SetInput(1,float.NaN,0,false);f.game.Step(.1f);Check(!float.IsNaN(f.Player(1).x),"finite position");
            });
            Test("Five thousand adversarial actions preserve ownership",()=>{
                var f=new Fixture();var random=new Random(823);var stations=f.game.Stations;
                for(int i=0;i<5000;i++) {
                    int p=random.Next(2)+1;int target=stations[random.Next(stations.Count)].id;f.At(p,target);
                    var r=f.Cmd(p,random.Next(4)==0?"cut":"interact",target);f.game.Command(p,r);
                    if(random.Next(3)==0) f.game.Command(p,r);
                    f.game.Step(.01f);f.game.CheckInvariants();
                }
            });
            Test("Single host can ready and start a full round",()=>{
                var f=new Fixture(false);f.game.RemovePlayer(2);f.Do(1,"ready",ready:true);
                Check(f.Do(1,"start").ok,"one-player start");f.game.Step(3.1f);
                Check(f.game.Phase==Phase.Playing&&f.game.Orders.Count==4,"solo orders");
                f.game.Step(301);Check(f.game.Result.participants==1,"solo settlement");
            });
            Test("Unordered recipes preserve duplicate ingredient quantities",()=>{
                Check(KitchenServer.RecipeMatches(new[]{"Bread","CheeseSlices","CheeseSlices"},new[]{"CheeseSlices","Bread","CheeseSlices"}),"unordered duplicate");
                Check(!KitchenServer.RecipeMatches(new[]{"Bread","CheeseSlices","CheeseSlices"},new[]{"CheeseSlices","Bread"}),"missing portion");
                Check(!KitchenServer.RecipeMatches(new[]{"Bread","CheeseSlices"},new[]{"Bread","TomatoSlices"}),"wrong ingredient");
            });
            Test("Bread asset is used once per recipe",()=>{
                var f=new Fixture();f.game.Rules.recipes[0].ingredients=new[]{"Bread","Bread"};
                bool rejected=false;try{f.game.Rules.Validate();}catch(ArgumentException){rejected=true;}
                Check(rejected,"double bread rejected");
            });
            Test("Tick input redundancy is idempotent and cannot speed up simulation",()=>{
                var f=new Fixture();f.game.UseTickInputs=true;f.Player(2).x=100;
                var inputs=Enumerable.Range(1,8).Select(i=>new InputFrame{epoch=f.game.Epoch,sequence=i,x=1}).ToArray();
                f.game.QueueInputs(1,inputs);f.game.QueueInputs(1,inputs);f.game.Step(Movement.TickSeconds);
                Check(Math.Abs(f.Player(1).x-7f/30)<.0001f&&f.Player(1).lastInput==1,"one input per server tick");
                for(int i=0;i<7;i++)f.game.Step(Movement.TickSeconds);
                Check(f.Player(1).lastInput==8&&Math.Abs(f.Player(1).x-8*7f/30)<.0001f,"redundant frames not repeated");
                f.game.QueueInputs(1,new[]{new InputFrame{epoch=f.game.Epoch-1,sequence=9,x=1},new InputFrame{epoch=f.game.Epoch,sequence=long.MaxValue,x=1}});
                f.game.Step(Movement.TickSeconds);Check(f.Player(1).lastInput==8,"old epochs and impossible future frames ignored");
            });
            Test("Prediction reconciles acknowledged input and replays remaining movement",()=>{
                var rules=new Rules();var prediction=new MovementPrediction();var p=new PlayerState{id=1};prediction.Reset(p,4);
                for(int i=0;i<4;i++)prediction.Advance(1,0,false,rules,null,new PlayerState[0]);
                Check(prediction.Position.x>.9f&&p.x==0,"local movement before host reply");
                p.x=2*7f/30;p.lastInput=2;prediction.Reconcile(p,rules,null,new PlayerState[0]);
                Check(prediction.PendingCount==2&&Math.Abs(prediction.Position.x-4*7f/30)<.0001f,"remaining frames replayed");
                p.x=0;p.lastInput=1;prediction.Reconcile(p,rules,null,null);
                Check(prediction.Position.x>.9f,"stale acknowledgement ignored");
                p.lastInput=4;p.x=.2f;prediction.Reconcile(p,rules,null,null);
                Check(prediction.PendingCount==0&&Math.Abs(prediction.Position.x-.2f)<.0001f,"host collision correction wins");
            });
            Test("World delta transfers mutations removals and stock without rewinding motion",()=>{
                var f=new Fixture();var builder=new WorldDeltaBuilder();var initial=f.game.GetSnapshot();builder.Create(initial);
                var client=new Snapshot{tick=100,players=initial.players.Select(p=>new PlayerState{id=p.id,x=99}).ToArray(),
                    stations=initial.stations.Select(v=>new StationState{id=v.id,plateStock=v.plateStock}).ToArray(),items=new ItemState[0]};
                f.Interact(1,13);var delta=builder.Create(f.game.GetSnapshot());delta.Apply(client);
                Check(client.items.Length==1&&client.stations.First(v=>v.id==13).plateStock==3,"stock and held plate transferred");
                Check(client.tick==100&&client.players[0].x==99,"motion not overwritten by control delta");
                f.Interact(1,15);builder.Create(f.game.GetSnapshot()).Apply(client);Check(client.items.Length==0,"removed item transferred");
                Check(builder.Create(f.game.GetSnapshot()).stations.Length==0,"unchanged stations omitted");
            });
            Test("Version fingerprints normalize Windows line endings and paths",()=>{
                var a=new[]{new KeyValuePair<string,string>("Assets/Code.cs","a\nb\n")};
                var b=new[]{new KeyValuePair<string,string>("Assets\\Code.cs","a\r\nb\r\n")};
                Check(BuildFingerprint.Compute(a)==BuildFingerprint.Compute(b),"LF and CRLF compatible");
                b[0]=new KeyValuePair<string,string>("Assets/Code.cs","changed");Check(BuildFingerprint.Compute(a)!=BuildFingerprint.Compute(b),"real source change rejected");
            });
            Test("Prediction survives delayed reordered and twenty percent dropped input packets",()=>{
                var f=new Fixture();f.game.UseTickInputs=true;f.Player(2).x=1000;
                var prediction=new MovementPrediction();prediction.Reset(f.Player(1),f.game.Epoch);
                var upstream=new List<Tuple<int,InputFrame[]>>();
                var downstream=new List<Tuple<int,PlayerState>>();
                for(int tick=0;tick<240;tick++) {
                    if(tick<180) {
                        prediction.Advance(tick<120?1:tick<150?-1:0,0,false,f.game.Rules,null,f.game.Players);
                        if(tick==0)Check(prediction.Position.x>0&&f.Player(1).x==0,"instant response before network reply");
                    }
                    // Simulate 100ms one-way input delay, jitter/reordering and 20% whole input-message loss.
                    if(tick<200&&tick%5!=1)upstream.Add(Tuple.Create(tick+3+(tick%7==0?2:0),prediction.Recent()));
                    foreach(var message in upstream.Where(v=>v.Item1<=tick).ToArray()) {
                        f.game.QueueInputs(1,message.Item2);upstream.Remove(message);
                    }
                    f.game.Step(Movement.TickSeconds);
                    var host=f.Player(1);var copy=new PlayerState{id=1};MotionState.From(host).Apply(copy);
                    downstream.Add(Tuple.Create(tick+3,copy));
                    foreach(var message in downstream.Where(v=>v.Item1<=tick).ToArray()) {
                        prediction.Reconcile(message.Item2,f.game.Rules,null,f.game.Players);downstream.Remove(message);
                    }
                }
                Check(f.Player(1).lastInput==180&&prediction.PendingCount==0,"all inputs recovered without duplicate motion");
                Check(Math.Abs(f.Player(1).x-90*7f/30)<.001f,"authoritative distance matches held keys");
                Check(Math.Abs(prediction.Position.x-f.Player(1).x)<.0001f,"prediction converges after acknowledgements");
            });
            Test("Actual bundled KCP UDP transport supports two clients and both channels",KcpRoundtrip);
            output("TOTAL "+count+" scenarios passed");return count;
        }
        private static void KcpRoundtrip() {
            var oldInfo=Log.Info;var oldWarning=Log.Warning;Log.Info=_=>{};Log.Warning=_=>{};
            var received=new List<string>();var peers=new HashSet<int>();
            KcpServer host=null;KcpClient a=null,b=null;
            try {
                var config=new KcpConfig(DualMode:false,Interval:10,SendWindowSize:256,ReceiveWindowSize:256);
                host=new KcpServer((id,ep)=>peers.Add(id),(id,bytes,channel)=>host.Send(id,bytes,channel),id=>{},(id,error,reason)=>{},config);
                a=new KcpClient(()=>{},(data,channel)=>received.Add("a:"+channel+":"+Encoding.UTF8.GetString(data.Array,data.Offset,data.Count)),()=>{},(error,reason)=>{},config);
                b=new KcpClient(()=>{},(data,channel)=>received.Add("b:"+channel+":"+Encoding.UTF8.GetString(data.Array,data.Offset,data.Count)),()=>{},(error,reason)=>{},config);
                host.Start(0);ushort port=(ushort)((IPEndPoint)host.LocalEndPoint).Port;a.Connect("127.0.0.1",port);b.Connect("127.0.0.1",port);
                Action pump=()=>{host.Tick();a.Tick();b.Tick();Thread.Sleep(1);};
                var limit=DateTime.UtcNow.AddSeconds(5);while((!a.connected||!b.connected)&&DateTime.UtcNow<limit)pump();
                Check(a.connected&&b.connected&&peers.Count==2,"two actual UDP peers");
                string unicode="番茄🍅"+new string('x',5000);
                a.Send(new ArraySegment<byte>(Encoding.UTF8.GetBytes(unicode)),KcpChannel.Reliable);
                b.Send(new ArraySegment<byte>(Encoding.UTF8.GetBytes("motion")),KcpChannel.Unreliable);
                limit=DateTime.UtcNow.AddSeconds(5);while(received.Count<2&&DateTime.UtcNow<limit)pump();
                Check(received.Contains("a:Reliable:"+unicode),"reliable fragmentation and UTF8 roundtrip");
                Check(received.Contains("b:Unreliable:motion"),"unreliable motion channel");
            } finally {if(a!=null)a.Disconnect();if(b!=null)b.Disconnect();if(host!=null)host.Stop();Log.Info=oldInfo;Log.Warning=oldWarning;}
        }
    }
}
