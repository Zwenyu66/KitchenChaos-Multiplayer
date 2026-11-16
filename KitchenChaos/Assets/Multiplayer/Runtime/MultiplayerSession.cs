using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KitchenChaos.Multiplayer {
    [DefaultExecutionOrder(-1000)]
    public sealed class MultiplayerSession : MonoBehaviour {
        public static MultiplayerSession Instance {get;private set;}
        public Snapshot State {get;private set;}
        public int LocalPlayer {get;private set;}
        public bool IsHost {get{return server!=null;}}
        public bool Connected {get{return State!=null&&LocalPlayer!=0;}}
        public bool Connecting {get;private set;}
        public string Notice {get;private set;}="创建房间可单人开局，也可邀请队友加入";
        public string Address {get;set;}="127.0.0.1";
        public string Nickname {get;set;}="厨师";
        public string RoomCode {get;set;}="";
        public string PortText {get;set;}="7777";
        public NetworkWorldView World {get;private set;}
        public Rules Config {get;private set;}
        public string ContentHash {get;private set;}
        public MovementPrediction Prediction {get;private set;}=new MovementPrediction();
        public float TickRemainder {get{return accumulator;}}
        public float RoundTripMs {get;private set;}
        public string NetworkInfo {get{return IsHost?"Mirror / KCP · 房主":"Mirror / KCP · RTT "+RoundTripMs.ToString("F0")+" ms";}}
        private MirrorLink transport;
        private KitchenServer server;
        private long sequence,lastMotionTick=-1;
        private float accumulator,snapshotTimer,fullTimer,heartbeatTimer,connectDeadline,rateTimer;
        private int knownEpoch=-1;
        private Phase knownPhase;
        private readonly HashSet<int> admitted=new HashSet<int>();
        private readonly Dictionary<int,float> deadlines=new Dictionary<int,float>();
        private readonly Dictionary<int,int> packetCounts=new Dictionary<int,int>();
        private readonly Dictionary<int,MotionState> latestMotion=new Dictionary<int,MotionState>();
        private readonly WorldDeltaBuilder deltas=new WorldDeltaBuilder();
        private sealed class PendingCommand {public int peer;public Packet packet;public float deadline;}
        private readonly List<PendingCommand> commands=new List<PendingCommand>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install() {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        private static void OnSceneLoaded(Scene scene,LoadSceneMode mode) {
            if (scene.name != "GameScene" || FindObjectOfType<MultiplayerSession>()!=null) return;
            // sceneLoaded fires after Awake, before Start. Original assets and serialized references survive.
            // Disable original single-player authority and UI before they subscribe or advance their timers.
            foreach (var m in FindObjectsOfType<MonoBehaviour>(true))
                if (m.GetType().Assembly==typeof(Player).Assembly) m.enabled=false;
            foreach (var canvas in FindObjectsOfType<Canvas>(true)) canvas.enabled=false;
            Time.timeScale=1;
            new GameObject("MultiplayerSession").AddComponent<MultiplayerSession>();
        }
        private void Awake() {
            Instance=this;Application.runInBackground=true;
            var ui=gameObject.AddComponent<MultiplayerUI>();ui.Session=this;
            try {
                var json=Resources.Load<TextAsset>("MultiplayerRules");
                if(json==null)throw new InvalidOperationException("缺少 MultiplayerRules.json");
                Config=JsonUtility.FromJson<Rules>(json.text);Config.Validate();
                World=gameObject.AddComponent<NetworkWorldView>();World.Initialize(this);
                var info=Resources.Load<TextAsset>("MultiplayerBuildInfo");
                if(info==null)throw new InvalidOperationException("缺少版本指纹，请运行 Validate Project");
                var build=JsonUtility.FromJson<BuildInfo>(info.text);
                string value="KitchenChaos-Mirror-KCP-v2\n"+build.sourceHash+"\n"+JsonUtility.ToJson(Config)+"\n"+World.LayoutSignature;
                using(var hash=SHA256.Create())ContentHash=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","");
                Nickname=PlayerPrefs.GetString("MP_Nickname","厨师");
            } catch(Exception ex) {Notice="初始化失败: "+ex.Message;Debug.LogException(ex);}
        }
        [Serializable] private class BuildInfo {public string sourceHash;}
        private bool ValidPort(out int port) {
            if(!int.TryParse(PortText,out port)||port<1024||port>65535) {Notice="端口需在1024至65535之间";return false;}
            return true;
        }
        private void CreateLink() {
            var root=new GameObject("Kitchen Mirror KCP");root.transform.SetParent(transform,false);transport=root.AddComponent<MirrorLink>();
        }
        public void HostRoom() {
            int port;if(World==null||string.IsNullOrEmpty(ContentHash)||transport!=null||!ValidPort(out port))return;
            try {
                CreateLink();transport.Host(port);
                RoomCode=Guid.NewGuid().ToString("N").Substring(0,6).ToUpperInvariant();
                server=new KitchenServer(Config,World.CreateStations(),RoomCode){ResolveMove=World.ResolveMove,UseTickInputs=true};
                var spawn=World.SpawnPosition(0);server.AddPlayer(1,Nickname,spawn.x,spawn.z);
                LocalPlayer=1;sequence=0;SaveName();ReceiveSnapshot(server.GetSnapshot());
                Notice="单人也可准备并开始。邀请队友时提供房主IPv4、UDP端口和房间码";
            } catch(Exception ex) {Leave("创建失败: "+ex.Message);}
        }
        public void JoinRoom() {
            int port;if(World==null||string.IsNullOrEmpty(ContentHash)||transport!=null||!ValidPort(out port))return;
            if(string.IsNullOrWhiteSpace(Address)||RoomCode.Trim().Length!=6){Notice="请填写房主地址和六位房间码";return;}
            try {
                CreateLink();Connecting=true;connectDeadline=Time.realtimeSinceStartup+10;sequence=0;SaveName();
                transport.Connect(Address.Trim(),port);Notice="正在通过 KCP 连接房主…";
            } catch(Exception ex){Leave("连接失败: "+ex.Message);}
        }
        private void SaveName(){Nickname=KitchenServer.CleanName(Nickname);PlayerPrefs.SetString("MP_Nickname",Nickname);}
        private void Update() {
            if(transport==null)return;
            rateTimer+=Time.unscaledDeltaTime;if(rateTimer>=1){rateTimer=0;packetCounts.Clear();}
            KitchenNetworkEvent e;int budget=256;
            while(budget-->0&&transport!=null&&transport.Poll(out e))HandleEvent(e);
            if(transport==null)return;
            if(Connecting&&Time.realtimeSinceStartup>connectDeadline){Leave("连接超时，请检查房主地址与 UDP 端口");return;}
            if(IsHost)foreach(var id in deadlines.Where(v=>Time.realtimeSinceStartup>v.Value).Select(v=>v.Key).ToArray()){
                deadlines.Remove(id);transport.Disconnect(id,"握手超时");
            }
            accumulator+=Math.Min(Time.unscaledDeltaTime,.2f);
            int steps=0;
            while(accumulator>=Movement.TickSeconds&&steps++<6) {
                accumulator-=Movement.TickSeconds;
                if(Connected&&State.phase==Phase.Playing) {
                    PreparePrediction();var input=World.ReadInput();
                    var frame=Prediction.Advance(input.x,input.y,World.RunPressed,Config,World.ResolveMove,State.players);
                    if(IsHost)server.QueueInputs(LocalPlayer,new[]{frame});else transport.SendInputs(Prediction.Recent());
                }
                if(IsHost) {
                    var previous=server.Phase;server.Step(Movement.TickSeconds);
                    ProcessCommands();
                    if(previous!=server.Phase)Broadcast(true);
                    var poses=server.Players.Select(MotionState.From).ToArray();
                    foreach(int peer in admitted)transport.SendMotion(peer,server.Epoch,server.TickNumber,poses);
                    if(Connected&&State.phase==Phase.Playing)Prediction.Reconcile(server.Players.First(v=>v.id==LocalPlayer),Config,World.ResolveMove,State.players);
                }
            }
            if(IsHost) {
                snapshotTimer-=Time.unscaledDeltaTime;fullTimer-=Time.unscaledDeltaTime;
                if(snapshotTimer<=0){snapshotTimer=.1f;Broadcast(fullTimer<=0);}
            }
            heartbeatTimer-=Time.unscaledDeltaTime;
            if(!IsHost&&Connected&&heartbeatTimer<=0){heartbeatTimer=1;Send(0,new Packet{type="ping",sentAt=Time.realtimeSinceStartup});}
        }
        private void PreparePrediction() {
            var local=State.players.FirstOrDefault(v=>v.id==LocalPlayer);if(local==null)return;
            if(Prediction.Position==null||Prediction.Epoch!=State.epoch)Prediction.Reset(local,State.epoch);
        }
        private void HandleEvent(KitchenNetworkEvent e) {
            if(e.type=="opened") {
                if(IsHost)deadlines[e.peer]=Time.realtimeSinceStartup+6;
                else Send(0,new Packet{type="hello",name=Nickname,code=RoomCode.Trim().ToUpperInvariant(),contentHash=ContentHash});
                return;
            }
            if(e.type=="closed"||e.type=="fault") {
                if(IsHost&&e.peer!=0) {
                    admitted.Remove(e.peer);deadlines.Remove(e.peer);commands.RemoveAll(c=>c.peer==e.peer);
                    server.RemovePlayer(e.peer);Broadcast(true);Notice="队友离开，手持物品已回收";
                } else Leave("连接结束: "+e.text);
                return;
            }
            if(e.type=="motion"){ReceiveMotion(e);return;}
            if(IsHost) {
                int count;packetCounts.TryGetValue(e.peer,out count);packetCounts[e.peer]=++count;
                if(count>240){transport.Disconnect(e.peer,"消息过于频繁");return;}
                if(e.type=="input") {if(admitted.Contains(e.peer))server.QueueInputs(e.peer,e.inputs);return;}
            }
            if(e.type!="control"||e.packet==null)return;
            var p=e.packet;
            if(IsHost) {
                if(!admitted.Contains(e.peer)) {
                    if(p.type!="hello")return;
                    string reason=null;
                    if(p.protocol!=2||p.contentHash!=ContentHash)reason="游戏版本或配置不同，请使用同一次构建的 Mirror 新版";
                    else if(p.code!=RoomCode)reason="房间码错误";
                    else if(server.Phase!=Phase.Lobby)reason="本局已开始，请等待返回房间";
                    else if(server.Players.Count>=Config.maxPlayers)reason="房间已满";
                    if(reason!=null){Send(e.peer,new Packet{type="reject",reason=reason},true);return;}
                    int slot=Enumerable.Range(0,4).First(v=>!server.Players.Any(a=>a.slot==v));
                    var spawn=World.SpawnPosition(slot);server.AddPlayer(e.peer,p.name,spawn.x,spawn.z);
                    admitted.Add(e.peer);deadlines.Remove(e.peer);transport.MarkAuthenticated(e.peer);
                    Send(e.peer,new Packet{type="welcome",player=e.peer});Broadcast(true);
                } else if(p.type=="command") {
                    server.QueueInputs(e.peer,e.inputs);
                    var player=server.Players.FirstOrDefault(v=>v.id==e.peer);
                    if(player!=null&&p.epoch==server.Epoch&&p.inputSequence>player.lastInput&&commands.Count<128)
                        commands.Add(new PendingCommand{peer=e.peer,packet=p,deadline=Time.realtimeSinceStartup+.5f});
                    else ExecuteCommand(e.peer,p);
                } else if(p.type=="ping")Send(e.peer,new Packet{type="pong",sentAt=p.sentAt});
                else if(p.type=="resync")Send(e.peer,new Packet{type="state",snapshot=server.GetSnapshot()});
            } else {
                if(p.type=="welcome"){LocalPlayer=p.player;Connecting=false;transport.MarkAuthenticated(0);Notice="已加入房间，请准备";}
                else if(p.type=="state")ReceiveSnapshot(p.snapshot);
                else if(p.type=="delta")ReceiveDelta(p.delta);
                else if(p.type=="ack")Notice=p.reason;
                else if(p.type=="reject")Leave("加入失败: "+p.reason);
                else if(p.type=="pong")RoundTripMs=Math.Max(0,(Time.realtimeSinceStartup-(float)p.sentAt)*1000);
            }
        }
        private void ProcessCommands() {
            foreach(var c in commands.ToArray()) {
                var p=server.Players.FirstOrDefault(v=>v.id==c.peer);
                if(p==null){commands.Remove(c);continue;}
                if(c.packet.epoch!=server.Epoch||p.lastInput>=c.packet.inputSequence||Time.realtimeSinceStartup>=c.deadline){
                    commands.Remove(c);ExecuteCommand(c.peer,c.packet);
                }
            }
        }
        private void ExecuteCommand(int peer,Packet p){Send(peer,server.Command(peer,p));Broadcast();}
        public void Request(string action,int target=0,int version=0,bool ready=false) {
            if(!Connected)return;
            var p=new Packet{type="command",seq=++sequence,epoch=State.epoch,action=action,target=target,
                expectedVersion=version,ready=ready,inputSequence=Prediction.Epoch==State.epoch?Prediction.Sequence:0};
            if(IsHost){Notice=server.Command(LocalPlayer,p).reason;Broadcast();}
            else transport.Send(0,p,Prediction.Recent());
        }
        private void Broadcast(bool full=false) {
            if(server==null)return;
            var state=server.GetSnapshot();
            full=full||state.epoch!=knownEpoch||state.phase!=knownPhase;
            knownEpoch=state.epoch;knownPhase=state.phase;
            if(full){fullTimer=3;deltas.Reset();}
            var delta=deltas.Create(state);ReceiveSnapshot(state);
            var packet=full?new Packet{type="state",snapshot=state}:new Packet{type="delta",delta=delta};
            foreach(int peer in admitted)Send(peer,packet);
        }
        private void ReceiveSnapshot(Snapshot s) {
            if(s==null||(State!=null&&s.revision<=State.revision))return;
            bool changed=State==null||s.epoch!=State.epoch;
            if(changed){lastMotionTick=-1;latestMotion.Clear();Prediction=new MovementPrediction();}
            if(s.phase==Phase.Playing&&s.epoch==knownEpoch&&lastMotionTick>s.tick)
                foreach(var p in s.players){MotionState m;if(latestMotion.TryGetValue(p.id,out m))m.Apply(p);}
            State=s;knownEpoch=s.epoch;World.Apply(s);
            PreparePrediction();
            if(!IsHost&&s.phase==Phase.Playing)Prediction.Reconcile(s.players.First(v=>v.id==LocalPlayer),Config,World.ResolveMove,s.players);
        }
        private void ReceiveDelta(WorldDelta d) {
            if(d==null||State==null||d.revision<=State.revision)return;
            if(d.epoch!=State.epoch){Send(0,new Packet{type="resync"});return;}
            d.Apply(State);World.Apply(State);
        }
        private void ReceiveMotion(KitchenNetworkEvent e) {
            if(IsHost||State==null||e.epoch!=State.epoch||e.tick<=lastMotionTick||e.players==null)return;
            lastMotionTick=e.tick;State.tick=e.tick;
            foreach(var m in e.players) {
                var p=State.players.FirstOrDefault(v=>v.id==m.id);if(p==null)continue;
                latestMotion[m.id]=m;m.Apply(p);
                if(m.id==LocalPlayer){PreparePrediction();Prediction.Reconcile(p,Config,World.ResolveMove,State.players);}
            }
        }
        private void Send(int peer,Packet packet,bool close=false){if(transport!=null)transport.Send(peer,packet,closeAfter:close);}
        public void Leave(string message="已退出房间") {
            if(transport!=null){transport.Close();Destroy(transport.gameObject);}transport=null;server=null;
            State=null;LocalPlayer=0;Connecting=false;admitted.Clear();deadlines.Clear();packetCounts.Clear();commands.Clear();
            latestMotion.Clear();deltas.Reset();Prediction=new MovementPrediction();knownEpoch=-1;lastMotionTick=-1;
            accumulator=0;Notice=message;if(World!=null)World.Clear();
        }
        private void OnDestroy(){Leave();if(Instance==this)Instance=null;}
        private void OnApplicationQuit(){Leave();}
        public void BackToMenu(){Leave();SceneManager.LoadScene("MainMenuScene");}
    }
}
