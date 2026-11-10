using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace KitchenChaos.Multiplayer {
    public sealed class TransportEvent {
        public int peer;
        public string type, text;
    }
    // Wire format: 4-byte big-endian byte length, followed by UTF-8 JSON.
    // Workers handle bytes only; JsonUtility, game commands and scene objects stay on the main thread.
    public sealed class TcpTransport : IDisposable {
        public const int MaxFrameBytes = 131072;
        private readonly ConcurrentDictionary<int, Peer> peers = new ConcurrentDictionary<int, Peer>();
        private readonly ConcurrentQueue<TransportEvent> events = new ConcurrentQueue<TransportEvent>();
        private TcpListener listener;
        private TcpClient connecting;
        private volatile bool disposed;
        private int nextPeer = 1;
        public int Port { get; private set; }
        private sealed class Outgoing { public string text; public bool close; }
        private sealed class Peer {
            public int id, closed;
            public TcpClient socket;
            public readonly BlockingCollection<Outgoing> outgoing = new BlockingCollection<Outgoing>(64);
        }
        public bool Poll(out TransportEvent e) { return events.TryDequeue(out e); }
        public void Host(int port) {
            listener = new TcpListener(IPAddress.Any,port); listener.Start(8);
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            StartThread(() => {
                while (!disposed) {
                    try {
                        var socket = listener.AcceptTcpClient();
                        if (peers.Count >= 7) { socket.Close(); continue; }
                        Attach(Interlocked.Increment(ref nextPeer),socket);
                    } catch (Exception ex) { if (!disposed) Push(0,"fault",ex.Message); break; }
                }
            });
        }
        public void Connect(string address,int port) {
            StartThread(() => {
                try {
                    var socket = new TcpClient(); connecting = socket;
                    var pending=socket.BeginConnect(address,port,null,null);
                    if (!pending.AsyncWaitHandle.WaitOne(5000)) { socket.Close(); throw new IOException("连接超时，请检查房主 IP 和防火墙"); }
                    socket.EndConnect(pending); pending.AsyncWaitHandle.Close(); connecting=null;
                    if (disposed) { socket.Close(); return; }
                    Attach(0,socket);
                } catch (Exception ex) { if (!disposed) Push(0,"fault",ex.Message); }
            });
        }
        private static void StartThread(ThreadStart action) { var thread=new Thread(action) { IsBackground=true }; thread.Start(); }
        private void Attach(int id,TcpClient socket) {
            socket.NoDelay=true; socket.ReceiveTimeout=10000; socket.SendTimeout=3000;
            var p=new Peer { id=id,socket=socket }; peers[id]=p;
            if (disposed) { Close(p,"连接已关闭"); return; }
            Push(id,"opened",null);
            StartThread(() => ReadLoop(p)); StartThread(() => WriteLoop(p));
        }
        private void Push(int peer,string type,string text) {
            if (disposed) return;
            if (type=="message" && events.Count >= 512) { Peer p; if (peers.TryGetValue(peer,out p)) Close(p,"消息队列过载"); return; }
            events.Enqueue(new TransportEvent { peer=peer,type=type,text=text });
        }
        private void ReadLoop(Peer p) {
            try {
                var stream=p.socket.GetStream(); var header=new byte[4];
                while (!disposed && p.closed == 0) {
                    ReadExact(stream,header,4);
                    int count=(header[0]<<24)|(header[1]<<16)|(header[2]<<8)|header[3];
                    if (count<=0 || count>MaxFrameBytes) throw new IOException("消息长度无效");
                    var bytes=new byte[count]; ReadExact(stream,bytes,count);
                    Push(p.id,"message",new UTF8Encoding(false,true).GetString(bytes));
                }
            } catch (Exception ex) { Close(p,ex is EndOfStreamException ? "对方已退出房间" : "连接中断或超时"); }
        }
        private static void ReadExact(Stream stream,byte[] bytes,int count) {
            int offset=0;
            while (offset<count) { int read=stream.Read(bytes,offset,count-offset); if (read==0) throw new EndOfStreamException(); offset+=read; }
        }
        private void WriteLoop(Peer p) {
            try {
                var stream=p.socket.GetStream();
                while (!disposed && p.closed==0) {
                    Outgoing message;
                    if (!p.outgoing.TryTake(out message,1000)) continue;
                    var bytes=Encoding.UTF8.GetBytes(message.text);
                    if (bytes.Length>MaxFrameBytes) throw new IOException("消息过大");
                    int n=bytes.Length;
                    var header=new[]{(byte)(n>>24),(byte)(n>>16),(byte)(n>>8),(byte)n};
                    stream.Write(header,0,4); stream.Write(bytes,0,n);
                    if (message.close) { Close(p,"房间拒绝连接"); break; }
                }
            } catch (Exception) { Close(p,"消息发送失败"); }
        }
        public void Send(int id,string text,bool closeAfter=false) {
            Peer p;
            if (!peers.TryGetValue(id,out p) || p.closed!=0) return;
            if (!p.outgoing.TryAdd(new Outgoing { text=text,close=closeAfter })) Close(p,"发送队列过载");
        }
        public void Disconnect(int id,string reason) { Peer p; if (peers.TryGetValue(id,out p)) Close(p,reason); }
        private void Close(Peer p,string reason) {
            if (Interlocked.Exchange(ref p.closed,1)!=0) return;
            Peer removed; peers.TryRemove(p.id,out removed);
            p.socket.Close(); Push(p.id,"closed",reason);
        }
        public void Dispose() {
            if (disposed) return; disposed=true;
            if (listener!=null) listener.Stop();
            if (connecting!=null) connecting.Close();
            foreach (var p in peers.Values) Close(p,"退出房间");
            TransportEvent e; while (events.TryDequeue(out e)) { }
        }
    }
}
