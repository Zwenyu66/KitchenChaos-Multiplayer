using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KitchenChaos.Multiplayer {
    [Serializable] public class PlayerStatus {
        public int id,hand,slot;
        public string name;
        public bool ready;
    }
    [Serializable] public class WorldDelta {
        public long revision,tick,eventId;
        public int epoch,lastActor,lastStation,lastReward;
        public float remaining,countdown;
        public string lastEvent;
        public PlayerStatus[] players;
        public StationState[] stations;
        public ItemState[] items;
        public int[] removedItems;
        public Order[] orders;
        public Summary summary;
        public void Apply(Snapshot s) {
            s.revision=revision;s.remaining=remaining;s.countdown=countdown;
            s.eventId=eventId;s.lastActor=lastActor;s.lastStation=lastStation;s.lastReward=lastReward;s.lastEvent=lastEvent;
            foreach(var p in players) {
                var current=s.players.FirstOrDefault(v=>v.id==p.id);
                if(current!=null) {current.hand=p.hand;current.ready=p.ready;current.name=p.name;current.slot=p.slot;}
            }
            foreach(var v in stations) { int i=Array.FindIndex(s.stations,a=>a.id==v.id);if(i>=0) s.stations[i]=v; }
            var list=s.items.Where(v=>!removedItems.Contains(v.id)).ToList();
            foreach(var v in items) {int i=list.FindIndex(a=>a.id==v.id);if(i>=0)list[i]=v;else list.Add(v);}
            s.items=list.ToArray();s.orders=orders;s.summary=summary;
        }
    }
    public sealed class WorldDeltaBuilder {
        private readonly Dictionary<int,string> stations=new Dictionary<int,string>(),items=new Dictionary<int,string>();
        private static string F(float v) {return v.ToString("R",CultureInfo.InvariantCulture);}
        public WorldDelta Create(Snapshot s) {
            var changedStations=new List<StationState>();
            foreach(var v in s.stations) {
                string key=v.item+"|"+v.version+"|"+v.cuts+"|"+v.plateStock+"|"+F(v.progress)+"|"+F(v.elapsed);
                string old;if(!stations.TryGetValue(v.id,out old)||old!=key)changedStations.Add(v);stations[v.id]=key;
            }
            var changedItems=new List<ItemState>();
            foreach(var v in s.items) {
                string key=v.kind+"|"+v.owner+"|"+string.Join(",",v.ingredients);
                string old;if(!items.TryGetValue(v.id,out old)||old!=key)changedItems.Add(v);items[v.id]=key;
            }
            var removed=items.Keys.Where(id=>!s.items.Any(v=>v.id==id)).ToArray();foreach(int id in removed)items.Remove(id);
            return new WorldDelta{revision=s.revision,tick=s.tick,epoch=s.epoch,remaining=s.remaining,countdown=s.countdown,
                eventId=s.eventId,lastEvent=s.lastEvent,lastActor=s.lastActor,lastStation=s.lastStation,lastReward=s.lastReward,
                players=s.players.Select(v=>new PlayerStatus{id=v.id,hand=v.hand,slot=v.slot,name=v.name,ready=v.ready}).ToArray(),
                stations=changedStations.ToArray(),items=changedItems.ToArray(),removedItems=removed,orders=s.orders,summary=s.summary};
        }
        public void Reset() {stations.Clear();items.Clear();}
    }
}
