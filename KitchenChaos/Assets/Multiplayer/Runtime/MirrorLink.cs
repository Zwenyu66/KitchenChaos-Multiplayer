using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using kcp2k;

namespace KitchenChaos.Multiplayer {
    public struct KitchenControlMessage : NetworkMessage { public string json;public InputFrame[] inputs; }
    public struct KitchenInputMessage : NetworkMessage { public InputFrame[] inputs; }
    public struct KitchenMotionMessage : NetworkMessage { public int epoch;public long tick;public MotionState[] players; }
    public sealed class KitchenNetworkEvent {
        public int peer,epoch;
        public long tick;
        public string type,text;
        public Packet packet;
        public InputFrame[] inputs;
        public MotionState[] players;
    }
    // All callbacks execute in Mirror's Unity player loop. The game keeps its own authority and DTOs.
    public sealed class MirrorLink : MonoBehaviour {
        private KcpTransport kcp;
        private bool hosting,closing;
        private readonly Queue<KitchenNetworkEvent> events=new Queue<KitchenNetworkEvent>();
        private readonly Dictionary<int,float> pendingDisconnects=new Dictionary<int,float>();
        private readonly Dictionary<int,int> peerConnections=new Dictionary<int,int>(),connectionPeers=new Dictionary<int,int>();
        private int nextPeer=2;
        public int SentBytes {get;private set;}
        public int ReceivedBytes {get;private set;}
        private void Awake() {
            var child=new GameObject("KCP Transport");child.SetActive(false);child.transform.SetParent(transform,false);
            kcp=child.AddComponent<KcpTransport>();
            kcp.NoDelay=true;kcp.Interval=10;kcp.DualMode=false;
            kcp.ReceiveWindowSize=kcp.SendWindowSize=256;
            Transport.active=kcp;
            child.SetActive(true);
        }
        public void Host(int port) {
            hosting=true;closing=false;kcp.Port=(ushort)port;NetworkServer.tickRate=60;
            NetworkServer.OnConnectedEvent=connection=> {
                // KCP endpoint hashes may be negative. Logical player IDs remain positive and never collide with host 1.
                int peer=nextPeer++;peerConnections[peer]=connection.connectionId;connectionPeers[connection.connectionId]=peer;
                Push(new KitchenNetworkEvent{type="opened",peer=peer});
            };
            NetworkServer.OnDisconnectedEvent=connection=> {
                int peer=Peer(connection.connectionId);
                Push(new KitchenNetworkEvent{type="closed",peer=peer,text="队友已断开"});
                connectionPeers.Remove(connection.connectionId);peerConnections.Remove(peer);pendingDisconnects.Remove(peer);
            };
            NetworkServer.OnErrorEvent=(connection,error,reason)=>Push(new KitchenNetworkEvent{type="fault",peer=connection==null?0:Peer(connection.connectionId),text=reason});
            NetworkServer.Listen(7);
            NetworkServer.RegisterHandler<KitchenControlMessage>((connection,message)=>ReceiveControl(Peer(connection.connectionId),message),false);
            NetworkServer.RegisterHandler<KitchenInputMessage>((connection,message)=>Push(new KitchenNetworkEvent{
                type="input",peer=Peer(connection.connectionId),inputs=message.inputs}),false);
        }
        private int Peer(int connection) {int peer;return connectionPeers.TryGetValue(connection,out peer)?peer:0;}
        private bool Connection(int peer,out NetworkConnectionToClient connection) {
            int id;connection=null;return peerConnections.TryGetValue(peer,out id)&&NetworkServer.connections.TryGetValue(id,out connection);
        }
        public void Connect(string address,int port) {
            hosting=false;closing=false;kcp.Port=(ushort)port;
            NetworkClient.OnConnectedEvent=()=>Push(new KitchenNetworkEvent{type="opened"});
            NetworkClient.OnDisconnectedEvent=()=>Push(new KitchenNetworkEvent{type="closed",text="与房主的连接中断或超时"});
            NetworkClient.OnErrorEvent=(error,reason)=>Push(new KitchenNetworkEvent{type="fault",text=reason});
            NetworkClient.Connect(address);
            NetworkClient.RegisterHandler<KitchenControlMessage>(message=>ReceiveControl(0,message),false);
            NetworkClient.RegisterHandler<KitchenMotionMessage>(message=> {
                ReceivedBytes+=20+(message.players==null?0:message.players.Length*40);
                Push(new KitchenNetworkEvent{type="motion",epoch=message.epoch,tick=message.tick,players=message.players});
            },false);
        }
        private void ReceiveControl(int peer,KitchenControlMessage message) {
            if(string.IsNullOrEmpty(message.json)||message.json.Length>131072) {Disconnect(peer,"消息长度无效");return;}
            try {
                var p=JsonUtility.FromJson<Packet>(message.json);
                if(p==null||p.type==null)throw new FormatException();
                ReceivedBytes+=System.Text.Encoding.UTF8.GetByteCount(message.json);
                Push(new KitchenNetworkEvent{type="control",peer=peer,packet=p,inputs=message.inputs});
            } catch(Exception) {Disconnect(peer,"消息格式无效");}
        }
        private void Push(KitchenNetworkEvent e) {
            if(closing)return;
            if(events.Count>=1024) {Disconnect(e.peer,"接收队列过载");return;}
            events.Enqueue(e);
        }
        public bool Poll(out KitchenNetworkEvent e) {e=events.Count==0?null:events.Dequeue();return e!=null;}
        public void Send(int peer,Packet packet,InputFrame[] inputs=null,bool closeAfter=false) {
            if(closing)return;
            string json=JsonUtility.ToJson(packet);
            var msg=new KitchenControlMessage{json=json,inputs=inputs};
            if(hosting) {
                NetworkConnectionToClient connection;
                if(!Connection(peer,out connection))return;
                connection.Send(msg,Channels.Reliable);
                // Allow a rejection packet to be flushed and acknowledged before closing the connection.
                if(closeAfter)pendingDisconnects[peer]=Time.realtimeSinceStartup+.75f;
            } else if(NetworkClient.isConnected)NetworkClient.Send(msg,Channels.Reliable);
            SentBytes+=System.Text.Encoding.UTF8.GetByteCount(json);
        }
        public void SendInputs(InputFrame[] inputs) {
            if(!closing&&NetworkClient.isConnected)NetworkClient.Send(new KitchenInputMessage{inputs=inputs},Channels.Unreliable);
            SentBytes+=8+(inputs==null?0:inputs.Length*25);
        }
        public void SendMotion(int peer,int epoch,long tick,MotionState[] players) {
            NetworkConnectionToClient connection;
            if(!closing&&Connection(peer,out connection)) {
                connection.Send(new KitchenMotionMessage{epoch=epoch,tick=tick,players=players},Channels.Unreliable);
                SentBytes+=20+players.Length*40;
            }
        }
        public void Disconnect(int peer,string reason) {
            if(hosting) {
                NetworkConnectionToClient connection;if(Connection(peer,out connection))connection.Disconnect();
            } else {NetworkClient.Disconnect();}
        }
        public void MarkAuthenticated(int peer) {
            if(hosting) {NetworkConnectionToClient c;if(Connection(peer,out c))c.isAuthenticated=true;}
            else if(NetworkClient.connection!=null)NetworkClient.connection.isAuthenticated=true;
        }
        private void Update() {
            if(!hosting||closing)return;
            var expired=new List<int>();
            foreach(var pair in pendingDisconnects)if(Time.realtimeSinceStartup>=pair.Value)expired.Add(pair.Key);
            foreach(int peer in expired) {pendingDisconnects.Remove(peer);Disconnect(peer,"房间拒绝加入");}
        }
        public void Close() {
            if(closing)return;closing=true;
            if(hosting)NetworkServer.Shutdown();else {NetworkClient.Disconnect();NetworkClient.Shutdown();}
            events.Clear();pendingDisconnects.Clear();peerConnections.Clear();connectionPeers.Clear();
            if(kcp!=null)kcp.Shutdown();
            if(kcp!=null)kcp.enabled=false;
        }
        private void OnDestroy() {Close();}
    }
}
