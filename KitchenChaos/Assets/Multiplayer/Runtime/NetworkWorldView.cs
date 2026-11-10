using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KitchenChaos.Multiplayer {
    public sealed class NetworkWorldView : MonoBehaviour {
        public string LayoutSignature { get; private set; }
        public bool PauseOverlay { get; set; }
        public bool RunPressed { get { return !PauseOverlay && Keyboard.current!=null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed); } }
        public int SelectedStation { get; private set; }
        public string Feedback { get; private set; }
        public float FeedbackUntil { get; private set; }
        public readonly Dictionary<int,BaseCounter> Counters=new Dictionary<int,BaseCounter>();
        public readonly Dictionary<string,KitchenObjectSO> Assets=new Dictionary<string,KitchenObjectSO>();
        private sealed class Chef {
            public Transform root,hold;
            public Animator animator;
            public float steps;
        }
        private sealed class ItemVisual { public Transform root; public string signature; }
        private readonly Dictionary<int,Chef> chefs=new Dictionary<int,Chef>();
        private readonly Dictionary<int,ItemVisual> visuals=new Dictionary<int,ItemVisual>();
        private readonly Dictionary<int,AudioSource> stoves=new Dictionary<int,AudioSource>();
        private readonly List<Vector3> spawns=new List<Vector3>();
        private readonly List<StationState> layout=new List<StationState>();
        private MultiplayerSession session;
        private Player template;
        private Vector3 holdPosition;
        private AudioClipRefsSO sounds;
        private Material ringTemplate;
        private readonly List<Material> ringMaterials=new List<Material>();
        private Snapshot state;
        private long lastEvent;
        private int lastSecond=-1;
        public static readonly Color[] ChefColors={new Color(1,.42f,.2f),new Color(.15f,.75f,.8f),new Color(.95f,.78f,.2f),new Color(.65f,.4f,.9f)};
        public void Initialize(MultiplayerSession owner) {
            session=owner;
            var catalog=Resources.Load<MultiplayerAssets>("MultiplayerAssets");
            if(catalog==null || catalog.items==null) throw new InvalidOperationException("缺少资源目录 MultiplayerAssets.asset");
            foreach(var item in catalog.items) Assets.Add(item.name,item);
            sounds=catalog.sounds;
            ringTemplate=catalog.playerRingMaterial;
            foreach(string id in new[]{"Bread","Tomato","TomatoSlices","Cabbage","CabbageSlices","CheeseBlock","CheeseSlices","MeatPattyUncooked","MeatPattyCooked","MeatPattyBurned","Plate"})
                if(!Assets.ContainsKey(id)) throw new InvalidOperationException("缺少食材资源: "+id);
            template=FindObjectOfType<Player>(true);
            if(template==null) throw new InvalidOperationException("GameScene中缺少Player美术模板");
            holdPosition=template.GetKitchenObjectFollowTransform().localPosition;
            foreach(var collider in template.GetComponentsInChildren<Collider>(true)) collider.enabled=false;
            Vector3 center=template.transform.position;
            // Choose actual free floor points close to the original spawn; do not hard-code a different kitchen layout.
            for(int ring=0;ring<8 && spawns.Count<4;ring++) {
                for(int x=-ring;x<=ring && spawns.Count<4;x++) for(int z=-ring;z<=ring && spawns.Count<4;z++) {
                    if(ring>0 && Math.Abs(x)!=ring && Math.Abs(z)!=ring) continue;
                    var candidate=center+new Vector3(x*1.25f,0,z*1.25f);
                    if(IsFree(candidate) && spawns.All(p=>Vector3.Distance(p,candidate)>1)) spawns.Add(candidate);
                }
            }
            if(spawns.Count<4) throw new InvalidOperationException("厨房内未找到四个安全出生点");
            template.gameObject.SetActive(false);
            var counters=FindObjectsOfType<BaseCounter>(true).OrderBy(c=>PathOf(c.transform),StringComparer.Ordinal).ToArray();
            int index=1;
            foreach(var counter in counters) {
                var kind=counter is ContainerCounter ? StationKind.Supply : counter is CuttingCounter ? StationKind.Cutting :
                    counter is StoveCounter ? StationKind.Stove : counter is PlatesCounter ? StationKind.Plates :
                    counter is DeliveryCounter ? StationKind.Delivery : counter is TrashCounter ? StationKind.Trash : StationKind.Clear;
                string supply=kind==StationKind.Supply ? ((ContainerCounter)counter).MultiplayerSupply.name : "";
                var s=new StationState{id=index,kind=kind,key=PathOf(counter.transform),x=counter.transform.position.x,z=counter.transform.position.z,supply=supply};
                layout.Add(s); Counters.Add(index,counter);
                if(kind==StationKind.Stove) {
                    var audio=counter.gameObject.AddComponent<AudioSource>();audio.playOnAwake=false;audio.loop=true;audio.spatialBlend=.3f;audio.volume=.2f;
                    if(sounds!=null) audio.clip=sounds.stoveSizzle;stoves.Add(index,audio);
                }
                index++;
            }
            LayoutSignature=string.Join("\n",layout.Select(s=>s.key+"|"+s.kind+"|"+s.supply+"|"+s.x.ToString("F3",CultureInfo.InvariantCulture)+","+s.z.ToString("F3",CultureInfo.InvariantCulture)));
            Clear();
        }
        private static string PathOf(Transform t) {
            string path="";
            while(t!=null) { path="/"+t.name+"["+t.GetSiblingIndex()+"]"+path;t=t.parent; }
            return path;
        }
        public IEnumerable<StationState> CreateStations() {
            return layout.Select(s=>new StationState{id=s.id,key=s.key,kind=s.kind,supply=s.supply,x=s.x,z=s.z}).ToArray();
        }
        public Vector3 SpawnPosition(int slot) { return spawns[slot%spawns.Count]; }
        private static bool IsFree(Vector3 p) {
            return !Physics.CheckCapsule(p+Vector3.up*.48f,p+Vector3.up*1.5f,.44f,~0,QueryTriggerInteraction.Ignore);
        }
        public float[] ResolveMove(PlayerState p,float x,float z) {
            var start=new Vector3(p.x,0,p.z);var delta=new Vector3(x-p.x,0,z-p.z);
            if(CanMove(start,delta)) return new[]{x,z};
            var dx=new Vector3(delta.x,0,0); if(CanMove(start,dx)) return new[]{x,p.z};
            var dz=new Vector3(0,0,delta.z); if(CanMove(start,dz)) return new[]{p.x,z};
            return new[]{p.x,p.z};
        }
        private static bool CanMove(Vector3 start,Vector3 delta) {
            if(delta.sqrMagnitude<.000001f) return true;
            return !Physics.CapsuleCast(start+Vector3.up*.48f,start+Vector3.up*1.5f,.44f,delta.normalized,
                delta.magnitude+.025f,~0,QueryTriggerInteraction.Ignore) && IsFree(start+delta);
        }
        public Vector2 ReadInput() {
            var k=Keyboard.current;
            if(k==null || PauseOverlay || !Application.isFocused || state==null || state.phase!=Phase.Playing) return Vector2.zero;
            float x=(k.dKey.isPressed||k.rightArrowKey.isPressed?1:0)-(k.aKey.isPressed||k.leftArrowKey.isPressed?1:0);
            float z=(k.wKey.isPressed||k.upArrowKey.isPressed?1:0)-(k.sKey.isPressed||k.downArrowKey.isPressed?1:0);
            return new Vector2(x,z).normalized;
        }
        public void Apply(Snapshot snapshot) {
            state=snapshot;
            foreach(var id in chefs.Keys.Where(id=>!state.players.Any(p=>p.id==id)).ToArray()) {
                foreach(var visual in visuals.Values) if(visual.root.parent==chefs[id].hold) visual.root.SetParent(transform,true);
                Destroy(chefs[id].root.gameObject);chefs.Remove(id);
            }
            foreach(var player in state.players) if(!chefs.ContainsKey(player.id)) CreateChef(player);
            foreach(var id in visuals.Keys.Where(id=>!state.items.Any(item=>item.id==id)).ToArray()) { Destroy(visuals[id].root.gameObject);visuals.Remove(id); }
            foreach(var item in state.items) {
                string signature=item.kind+"|"+string.Join(",",item.ingredients);
                ItemVisual visual;
                if(!visuals.TryGetValue(item.id,out visual) || visual.signature!=signature) {
                    if(visual!=null) Destroy(visual.root.gameObject);
                    var root=Instantiate(Assets[item.kind].prefab);
                    foreach(var behavior in root.GetComponentsInChildren<MonoBehaviour>(true)) behavior.enabled=false;
                    foreach(var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled=false;
                    foreach(var canvas in root.GetComponentsInChildren<Canvas>(true)) canvas.enabled=false;
                    if(item.kind=="Plate") {
                        var plateVisual=root.GetComponentInChildren<PlateCompleteVisual>(true);
                        if(plateVisual!=null) plateVisual.ApplyMultiplayerIngredients(item.ingredients.Select(id=>Assets[id]).ToList());
                    }
                    visual=new ItemVisual{root=root,signature=signature};visuals[item.id]=visual;
                }
                Transform parent=null;int idNumber;
                if(item.owner.StartsWith("p:") && int.TryParse(item.owner.Substring(2),out idNumber) && chefs.ContainsKey(idNumber)) parent=chefs[idNumber].hold;
                if(item.owner.StartsWith("c:") && int.TryParse(item.owner.Substring(2),out idNumber) && Counters.ContainsKey(idNumber)) parent=Counters[idNumber].GetKitchenObjectFollowTransform();
                if(parent!=null) { visual.root.SetParent(parent,false);visual.root.localPosition=Vector3.zero;visual.root.localRotation=Quaternion.identity; }
            }
            foreach(var pair in stoves) {
                var s=state.stations.First(v=>v.id==pair.Key);var item=state.items.FirstOrDefault(i=>i.id==s.item);
                bool active=state.phase==Phase.Playing && item!=null && (item.kind=="MeatPattyUncooked"||item.kind=="MeatPattyCooked");
                if(active && !pair.Value.isPlaying && pair.Value.clip!=null) pair.Value.Play(); else if(!active) pair.Value.Stop();
                var stove=Counters[pair.Key].GetComponentInChildren<StoveCounterVisual>(true);
                if(stove!=null) stove.ApplyMultiplayerState(active);
            }
            if(snapshot.eventId>lastEvent) { lastEvent=snapshot.eventId;PlayEvent(snapshot); }
        }
        private void CreateChef(PlayerState player) {
            var root=new GameObject("Chef_"+player.id).transform;
            root.position=new Vector3(player.x,0,player.z);
            foreach(Transform child in template.transform) {
                if(child==template.GetKitchenObjectFollowTransform()) continue;
                var visual=Instantiate(child,root,false);
                foreach(var behavior in visual.GetComponentsInChildren<MonoBehaviour>(true)) behavior.enabled=false;
                foreach(var collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled=false;
            }
            var hold=new GameObject("Hand").transform;hold.SetParent(root,false);hold.localPosition=holdPosition;
            var ring=GameObject.CreatePrimitive(PrimitiveType.Cylinder);ring.name="PlayerColour";ring.transform.SetParent(root,false);
            ring.transform.localPosition=new Vector3(0,.025f,0);ring.transform.localScale=new Vector3(1.05f,.018f,1.05f);
            ring.GetComponent<Collider>().enabled=false;
            var material=ringTemplate!=null?new Material(ringTemplate):ring.GetComponent<Renderer>().material;
            material.color=ChefColors[player.slot%4];ring.GetComponent<Renderer>().material=material;
            ringMaterials.Add(material);
            chefs.Add(player.id,new Chef{root=root,hold=hold,animator=root.GetComponentInChildren<Animator>(true)});
        }
        private void Update() {
            var k=Keyboard.current;
            if(k!=null && k.escapeKey.wasPressedThisFrame && session.Connected) PauseOverlay=!PauseOverlay;
            if(state==null) return;
            foreach(var p in state.players) {
                Chef chef;if(!chefs.TryGetValue(p.id,out chef)) continue;
                var goal=new Vector3(p.x,0,p.z);
                chef.root.position=Vector3.Distance(chef.root.position,goal)>3 ? goal : Vector3.Lerp(chef.root.position,goal,1-Mathf.Exp(-20*Time.unscaledDeltaTime));
                var facing=new Vector3(-p.facingX,0,-p.facingZ);
                if(facing.sqrMagnitude>.01f) chef.root.rotation=Quaternion.Slerp(chef.root.rotation,Quaternion.LookRotation(facing),Time.unscaledDeltaTime*20);
                if(chef.animator!=null) { chef.animator.SetBool("IsWalking",p.walking);chef.animator.SetBool("IsRunning",p.running); }
                chef.steps-=Time.unscaledDeltaTime;
                if(p.walking && chef.steps<=0) { chef.steps=p.running?.25f:.5f;Play(sounds==null?null:sounds.footstep,chef.root.position,.3f); }
            }
            SelectedStation=0;
            var local=state.players.FirstOrDefault(p=>p.id==session.LocalPlayer);
            if(local!=null && state.phase==Phase.Playing) {
                var forward=new Vector2(local.facingX,local.facingZ).normalized;
                float best=float.MaxValue;
                foreach(var s in state.stations) {
                    var delta=new Vector2(s.x-local.x,s.z-local.z);float distance=delta.magnitude;
                    if(distance<=session.Config.interactDistance && Vector2.Dot(delta.normalized,forward)>.25f) {
                        float rank=distance-Vector2.Dot(delta.normalized,forward)*.5f;
                        if(rank<best) { best=rank;SelectedStation=s.id; }
                    }
                }
                if(!PauseOverlay && k!=null && SelectedStation!=0) {
                    var selected=state.stations.First(s=>s.id==SelectedStation);
                    if(k.eKey.wasPressedThisFrame) session.Request("interact",selected.id,selected.version);
                    if(k.fKey.wasPressedThisFrame) session.Request("cut",selected.id,selected.version);
                }
                int second=Mathf.CeilToInt(state.remaining);
                if(second<=30 && second!=lastSecond) { lastSecond=second;Play(sounds==null?null:sounds.warning,Vector3.zero,.6f); }
            }
        }
        private void PlayEvent(Snapshot s) {
            if(s.lastEvent=="delivery"||s.lastEvent=="wrong") {
                Feedback=s.lastEvent=="delivery"?"✓ 出餐成功  +"+session.Config.successPoints:"出餐不符  -"+session.Config.wrongPenalty;
                FeedbackUntil=Time.unscaledTime+1.8f;
            }
            Vector3 position=s.lastStation!=0&&Counters.ContainsKey(s.lastStation)?Counters[s.lastStation].transform.position:Vector3.zero;
            if(sounds==null) return;
            if(s.lastEvent=="pickup") Play(sounds.objectPickup,position);
            if(s.lastEvent=="drop"||s.lastEvent=="plate") Play(sounds.objectDrop,position);
            if(s.lastEvent=="trash") Play(sounds.trash,position);
            if(s.lastEvent=="delivery") Play(sounds.deliverySuccess,position);
            if(s.lastEvent=="wrong") Play(sounds.deliveryFail,position);
            if(s.lastEvent=="cut") {
                Play(sounds.chop,position);
                if(Counters.ContainsKey(s.lastStation)) {
                    var anim=Counters[s.lastStation].GetComponentInChildren<CuttingCounterVisual>(true);
                    if(anim!=null && anim.GetComponent<Animator>()!=null) anim.GetComponent<Animator>().SetTrigger("Cut");
                }
            }
            if(s.lastEvent=="pickup" && Counters.ContainsKey(s.lastStation) && Counters[s.lastStation] is ContainerCounter) {
                var anim=Counters[s.lastStation].GetComponentInChildren<ContanerCounterVisual>(true);
                if(anim!=null && anim.GetComponent<Animator>()!=null) anim.GetComponent<Animator>().SetTrigger("OpenClose");
            }
        }
        private static void Play(AudioClip[] clips,Vector3 pos,float volume=.7f) { if(clips!=null && clips.Length>0) AudioSource.PlayClipAtPoint(clips[UnityEngine.Random.Range(0,clips.Length)],pos,volume); }
        public void Clear() {
            foreach(var chef in chefs.Values) if(chef.root!=null) Destroy(chef.root.gameObject);chefs.Clear();
            foreach(var item in visuals.Values) if(item.root!=null) Destroy(item.root.gameObject);visuals.Clear();
            foreach(var source in stoves.Values) source.Stop();
            foreach(var counter in Counters.Values) { var stove=counter.GetComponentInChildren<StoveCounterVisual>(true);if(stove!=null) stove.ApplyMultiplayerState(false); }
            state=null;lastEvent=0;lastSecond=-1;PauseOverlay=false;
            Feedback=null;FeedbackUntil=0;
            foreach(var material in ringMaterials) Destroy(material);ringMaterials.Clear();
        }
        public Vector3 ChefPosition(int id) { return chefs.ContainsKey(id)?chefs[id].root.position:Vector3.zero; }
        private void OnDestroy() { Clear(); }
    }
}
