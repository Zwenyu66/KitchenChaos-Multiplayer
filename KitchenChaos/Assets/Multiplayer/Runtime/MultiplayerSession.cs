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
        public static MultiplayerSession Instance { get; private set; }
        public Snapshot State { get; private set; }
        public int LocalPlayer { get; private set; }
        public bool IsHost { get { return server!=null; } }
        public bool Connected { get { return State!=null && LocalPlayer!=0; } }
        public bool Connecting { get; private set; }
        public string Notice { get; private set; } = "创建房间，或输入房主地址与房间码加入";
        public string Address { get; set; } = "127.0.0.1";
        public string Nickname { get; set; } = "厨师";
        public string RoomCode { get; set; } = "";
        public string PortText { get; set; } = "7777";
        public NetworkWorldView World { get; private set; }
        public Rules Config { get; private set; }
        public string ContentHash { get; private set; }
        private TcpTransport transport;
        private KitchenServer server;
        private long sequence;
        private float sendTimer,snapshotTimer,heartbeatTimer;
        private readonly HashSet<int> admitted = new HashSet<int>();
        private readonly Dictionary<int,float> deadlines = new Dictionary<int,float>();
        private readonly Dictionary<int,int> packetCounts = new Dictionary<int,int>();
        private float rateTimer;

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
            Instance=this; Application.runInBackground=true;
            var ui=gameObject.AddComponent<MultiplayerUI>(); ui.Session=this;
            try {
                var json=Resources.Load<TextAsset>("MultiplayerRules");
                if (json==null) throw new InvalidOperationException("缺少 MultiplayerRules.json");
                Config=JsonUtility.FromJson<Rules>(json.text); Config.Validate();
                World=gameObject.AddComponent<NetworkWorldView>(); World.Initialize(this);
                var buildInfo=Resources.Load<TextAsset>("MultiplayerBuildInfo");
                if(buildInfo==null) throw new InvalidOperationException("缺少版本指纹，请运行 Kitchen Chaos / Validate Project");
                var hashText="KitchenChaos-MP-v1\n"+buildInfo.text+"\n"+json.text+"\n"+World.LayoutSignature;
                using(var hash=SHA256.Create()) ContentHash=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(hashText))).Replace("-","");
                Nickname=PlayerPrefs.GetString("MP_Nickname","厨师");
            } catch(Exception ex) { Notice="初始化失败: "+ex.Message; Debug.LogException(ex); }
        }
        public void HostRoom() {
            if (World==null || string.IsNullOrEmpty(ContentHash) || transport!=null) return;
            int port; if (!int.TryParse(PortText,out port)||port<1024||port>65535) { Notice="端口需在1024到65535之间";return; }
            try {
                transport=new TcpTransport(); transport.Host(port);
                RoomCode=Guid.NewGuid().ToString("N").Substring(0,6).ToUpperInvariant();
                server=new KitchenServer(Config,World.CreateStations(),RoomCode);
                server.ResolveMove=World.ResolveMove;
                var spawn=World.SpawnPosition(0); server.AddPlayer(1,Nickname,spawn.x,spawn.z);
                LocalPlayer=1; sequence=0; SaveName(); ReceiveSnapshot(server.GetSnapshot());
                Notice="房间已创建。让队友输入你的局域网IPv4、端口和房间码";
            } catch(Exception ex) { Leave("创建失败: "+ex.Message); }
        }
        public void JoinRoom() {
            if (World==null || string.IsNullOrEmpty(ContentHash) || transport!=null) return;
            int port; if (!int.TryParse(PortText,out port)||port<1024||port>65535||string.IsNullOrWhiteSpace(Address)||RoomCode.Trim().Length!=6) {
                Notice="请填写有效的地址、端口和6位房间码"; return;
            }
            transport=new TcpTransport(); Connecting=true; sequence=0; SaveName();
            transport.Connect(Address.Trim(),port); Notice="正在连接房主…";
        }
        private void SaveName() { Nickname=KitchenServer.CleanName(Nickname); PlayerPrefs.SetString("MP_Nickname",Nickname); }
        private void Update() {
            if (transport==null) return;
            rateTimer+=Time.unscaledDeltaTime;
            if(rateTimer>=1) { rateTimer=0; packetCounts.Clear(); }
            TransportEvent e; int budget=128;
            while (budget-->0 && transport!=null && transport.Poll(out e)) HandleEvent(e);
            if(transport==null) return;
            if (server!=null) {
                foreach(var id in deadlines.Where(d=>Time.realtimeSinceStartup>d.Value).Select(d=>d.Key).ToArray()) {
                    deadlines.Remove(id); transport.Disconnect(id,"加入房间超时");
                }
                server.Step(Math.Min(Time.unscaledDeltaTime,.25f));
                snapshotTimer-=Time.unscaledDeltaTime;
                if(snapshotTimer<=0) { snapshotTimer=.1f; Broadcast(); }
            }
            sendTimer-=Time.unscaledDeltaTime;
            if(Connected && sendTimer<=0) {
                sendTimer=.05f; var input=World.ReadInput();
                var packet=new Packet{type="input",x=input.x,z=input.y,run=World.RunPressed};
                if(server!=null) server.SetInput(LocalPlayer,packet.x,packet.z,packet.run); else Send(0,packet);
            }
            heartbeatTimer-=Time.unscaledDeltaTime;
            if(heartbeatTimer<=0) { heartbeatTimer=1; if(server==null) Send(0,new Packet{type="ping"}); }
        }
        private void HandleEvent(TransportEvent e) {
            if(e.type=="opened") {
                if(IsHost) deadlines[e.peer]=Time.realtimeSinceStartup+6;
                else Send(0,new Packet{type="hello",name=Nickname,code=RoomCode.Trim().ToUpperInvariant(),contentHash=ContentHash});
                return;
            }
            if(e.type=="closed" || e.type=="fault") {
                if(IsHost && e.peer!=0) {
                    admitted.Remove(e.peer); deadlines.Remove(e.peer); server.RemovePlayer(e.peer); Broadcast();
                    Notice="一名玩家离开。手持物品已放回空台面，台面已满时清理";
                } else Leave("房间连接已结束: "+e.text);
                return;
            }
            if(e.type!="message") return;
            Packet packet;
            try { packet=JsonUtility.FromJson<Packet>(e.text); if(packet==null||packet.type==null) throw new FormatException(); }
            catch { transport.Disconnect(e.peer,"消息格式无效"); return; }
            if(IsHost) {
                int count;packetCounts.TryGetValue(e.peer,out count);packetCounts[e.peer]=++count;
                if(count>100) { transport.Disconnect(e.peer,"请求过于频繁");return; }
                if(!admitted.Contains(e.peer)) {
                    if(packet.type!="hello") return;
                    string reason=null;
                    if(packet.protocol!=1 || packet.contentHash!=ContentHash) reason="游戏版本或配置不同，请使用相同的完整项目构建";
                    else if(packet.code!=RoomCode) reason="房间码错误";
                    else if(server.Phase!=Phase.Lobby) reason="本局已经开始，请等待返回房间后再加入";
                    else if(server.Players.Count>=Config.maxPlayers) reason="房间已满";
                    if(reason!=null) { Send(e.peer,new Packet{type="reject",reason=reason},true);return; }
                    int slot=0;
                    for(;slot<4;slot++) { var pos=World.SpawnPosition(slot); if(!server.Players.Any(p=>Math.Abs(p.x-pos.x)<.2f && Math.Abs(p.z-pos.z)<.2f)) break; }
                    var spawn=World.SpawnPosition(Math.Min(slot,3));
                    server.AddPlayer(e.peer,packet.name,spawn.x,spawn.z); admitted.Add(e.peer); deadlines.Remove(e.peer);
                    Send(e.peer,new Packet{type="welcome",player=e.peer}); Broadcast();
                } else if(packet.type=="input") server.SetInput(e.peer,packet.x,packet.z,packet.run);
                else if(packet.type=="command") { var result=server.Command(e.peer,packet); Send(e.peer,result); Broadcast(); }
                else if(packet.type=="ping") Send(e.peer,new Packet{type="pong"});
            } else {
                if(packet.type=="welcome") { LocalPlayer=packet.player; Connecting=false; Notice="已加入房间，请准备"; }
                else if(packet.type=="state") ReceiveSnapshot(packet.snapshot);
                else if(packet.type=="ack") Notice=packet.reason;
                else if(packet.type=="reject") Leave("加入失败: "+packet.reason);
            }
        }
        public void Request(string action,int target=0,int version=0,bool ready=false) {
            if(!Connected) return;
            var r=new Packet{type="command",seq=++sequence,epoch=State.epoch,action=action,target=target,expectedVersion=version,ready=ready};
            if(server!=null) { Notice=server.Command(LocalPlayer,r).reason; Broadcast(); } else Send(0,r);
        }
        private void Broadcast() {
            if(server==null) return;
            var state=server.GetSnapshot(); ReceiveSnapshot(state);
            var text=JsonUtility.ToJson(new Packet{type="state",snapshot=state});
            foreach(var peer in admitted) transport.Send(peer,text);
        }
        private void ReceiveSnapshot(Snapshot state) {
            if(state==null || (State!=null && state.revision<=State.revision)) return;
            State=state; World.Apply(state);
        }
        private void Send(int peer,Packet p,bool close=false) { if(transport!=null) transport.Send(peer,JsonUtility.ToJson(p),close); }
        public void Leave(string message="已退出房间") {
            if(transport!=null) transport.Dispose(); transport=null; server=null; State=null;LocalPlayer=0;Connecting=false;
            admitted.Clear();deadlines.Clear();packetCounts.Clear();Notice=message;
            if(World!=null) World.Clear();
        }
        private void OnDestroy() { Leave(); if(Instance==this) Instance=null; }
        private void OnApplicationQuit() { Leave(); }
        public void BackToMenu() { Leave();SceneManager.LoadScene("MainMenuScene"); }
    }
}
