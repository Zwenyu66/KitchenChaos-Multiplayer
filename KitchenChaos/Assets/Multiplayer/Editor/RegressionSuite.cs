using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
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
                    new Recipe{name="汉堡",ingredients=new[]{"Bread","MeatPattyCooked"}},
                    new Recipe{name="沙拉",ingredients=new[]{"CabbageSlices","TomatoSlices"}},
                    new Recipe{name="双番茄",ingredients=new[]{"TomatoSlices","TomatoSlices"}},
                    new Recipe{name="双奶酪",ingredients=new[]{"Bread","CheeseSlices","CheeseSlices"}}
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
                    for(int i=0;i<game.Rules.cutCount;i++) { game.Step(game.Rules.cutInterval);Check(Do(player,"cut",11).ok,"cut"); }
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
            Test("Cutting cooldown and item progress",()=>{
                var f=new Fixture();f.Interact(1,2);f.Interact(1,11);
                Check(f.Do(1,"cut",11).ok,"first cut");Check(!f.Do(1,"cut",11).ok,"spam cut");
                for(int i=1;i<4;i++) { f.game.Step(f.game.Rules.cutInterval);Check(f.Do(1,"cut",11).ok,"subsequent cut"); }
                Check(f.game.FindItem(f.game.Stations.Find(s=>s.id==11).item).kind=="TomatoSlices","slices");
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
            Test("Wrong assembly order fails and plate is consumed once",()=>{
                var f=new Fixture();f.Plate(1,new[]{"MeatPattyCooked","Bread"});f.At(1,14);
                var r=f.Cmd(1,"interact",14);Check(f.game.Command(1,r).ok,"wrong delivery is processed");
                f.game.Command(1,r);Check(f.game.Result.wrong==1&&f.Player(1).hand==0,"idempotent wrong delivery");
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
            Test("TCP supports two clients and fragmented UTF8 frames",TransportRoundtrip);
            Test("TCP rejects oversized frames and observes host shutdown",TransportLimits);
            output("TOTAL "+count+" scenarios passed");return count;
        }
        private static TransportEvent Wait(TcpTransport t,Func<TransportEvent,bool> match) {
            var stop=DateTime.UtcNow.AddSeconds(5);
            while(DateTime.UtcNow<stop) { TransportEvent e;while(t.Poll(out e)) if(match(e)) return e;Thread.Sleep(5); }
            throw new Exception("transport timed out");
        }
        private static void TransportRoundtrip() {
            using(var host=new TcpTransport()) using(var a=new TcpTransport()) using(var b=new TcpTransport()) {
                host.Host(0);a.Connect("127.0.0.1",host.Port);b.Connect("127.0.0.1",host.Port);
                Wait(a,e=>e.type=="opened");Wait(b,e=>e.type=="opened");
                a.Send(0,"番茄🍅");b.Send(0,"B");
                var first=Wait(host,e=>e.type=="message");var second=Wait(host,e=>e.type=="message");
                Check(first.peer!=second.peer,"connection identities");
                host.Send(first.peer,first.text);host.Send(second.peer,second.text);
                Check(Wait(a,e=>e.type=="message").text=="番茄🍅","UTF8");Check(Wait(b,e=>e.type=="message").text=="B","second client");
                using(var raw=new TcpClient(AddressFamily.InterNetwork)) {
                    raw.Connect(System.Net.IPAddress.Loopback,host.Port);
                    var stream=raw.GetStream();var bytes=Encoding.UTF8.GetBytes("split frame");
                    var all=new byte[4+bytes.Length];all[3]=(byte)bytes.Length;Array.Copy(bytes,0,all,4,bytes.Length);
                    for(int i=0;i<all.Length;i++) stream.Write(all,i,1);
                    Check(Wait(host,e=>e.type=="message").text=="split frame","partial reads");
                }
            }
        }
        private static void TransportLimits() {
            using(var host=new TcpTransport()) using(var client=new TcpTransport()) {
                host.Host(0);
                using(var raw=new TcpClient(AddressFamily.InterNetwork)) {
                    raw.Connect(System.Net.IPAddress.Loopback,host.Port);
                    var opened=Wait(host,e=>e.type=="opened");raw.GetStream().Write(new byte[]{127,255,255,255},0,4);
                    Wait(host,e=>e.type=="closed"&&e.peer==opened.peer);
                }
                client.Connect("127.0.0.1",host.Port);Wait(client,e=>e.type=="opened");host.Dispose();
                Wait(client,e=>e.type=="closed");
            }
        }
    }
}
