using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KitchenChaos.Multiplayer {
    public sealed class MultiplayerUI : MonoBehaviour {
        public MultiplayerSession Session;
        private const float HudWidth=1440f,HudHeight=900f,TopBand=126f,BottomBand=88f;
        private const string Beef="MeatPattyUncooked";
        private GUIStyle title,heading,text,small,button,field,panel,center,orderTitle,number,tileNumber;
        private readonly Color panelBlue=new Color(.14f,.25f,.32f),ink=new Color(.95f,.97f,.92f),
            blue=new Color(.19f,.38f,.49f),strip=new Color(.075f,.15f,.19f),edge=new Color(.42f,.65f,.70f),
            tile=new Color(.60f,.76f,.79f),orange=new Color(1f,.73f,.34f),green=new Color(.48f,.75f,.31f),
            beefRed=new Color(.53f,.22f,.23f), warning=new Color(.56f,.24f,.23f);
        private float scale,marginX,marginY,resultStarted=-1;
        private bool showMenu,showHints=true;
        private Camera viewportCamera;
        private Rect originalViewport;
        private float Left { get { return -marginX/scale; } }
        private float Top { get { return -marginY/scale; } }
        private float Right { get { return (Screen.width-marginX)/scale; } }
        private float Bottom { get { return (Screen.height-marginY)/scale; } }
        private void Update() {
            if(Session==null || !Session.Connected || Session.State.phase!=Phase.Playing ||
                Session.World.PauseOverlay || !Application.isFocused) return;
            var keyboard=Keyboard.current;
            if(keyboard!=null && keyboard.hKey.wasPressedThisFrame) showHints=!showHints;
        }
        private static float HudScale() { return Mathf.Max(.01f,Mathf.Min(Screen.width/HudWidth,Screen.height/HudHeight)); }
        private static Rect MatchViewport() {
            // Keep both HUD strips outside the camera image. A narrow window also keeps the kitchen's aspect.
            float fit=HudScale(), top=TopBand*fit,bottom=BottomBand*fit;
            float available=Mathf.Max(1,Screen.height-top-bottom);
            float height=Mathf.Min(available,Screen.width/(16f/9f));
            return new Rect(0,(bottom+(available-height)*.5f)/Screen.height,1,height/Screen.height);
        }
        private void LateUpdate() {
            bool inMatch=Session!=null && Session.Connected &&
                (Session.State.phase==Phase.Playing || Session.State.phase==Phase.Countdown);
            Camera camera=inMatch?Camera.main:null;
            if(viewportCamera!=camera) {
                RestoreViewport();
                if(camera!=null) { viewportCamera=camera;originalViewport=camera.rect; }
            }
            if(viewportCamera!=null) {
                var rect=MatchViewport();
                if(!viewportCamera.rect.Equals(rect)) viewportCamera.rect=rect;
            }
        }
        private void RestoreViewport() {
            if(viewportCamera!=null) viewportCamera.rect=originalViewport;
            viewportCamera=null;
        }
        private void OnDisable() { RestoreViewport(); }
        private void CreateStyles() {
            Font font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Noto Sans CJK SC","SimHei","Arial"},20);
            text=new GUIStyle(GUI.skin.label){font=font,fontSize=19,wordWrap=true};text.normal.textColor=ink;
            small=new GUIStyle(text){fontSize=15};
            title=new GUIStyle(text){fontSize=38,fontStyle=FontStyle.Bold};
            heading=new GUIStyle(text){fontSize=25,fontStyle=FontStyle.Bold};
            center=new GUIStyle(heading){alignment=TextAnchor.MiddleCenter};
            number=new GUIStyle(center){fontSize=34};
            orderTitle=new GUIStyle(heading){fontSize=18,wordWrap=false};
            tileNumber=new GUIStyle(small){alignment=TextAnchor.MiddleCenter,fontSize=11};
            tileNumber.normal.textColor=strip;
            button=new GUIStyle(GUI.skin.button){font=font,fontSize=18,padding=new RectOffset(8,8,6,6)};
            field=new GUIStyle(GUI.skin.textField){font=font,fontSize=22,padding=new RectOffset(12,10,8,8)};
            panel=new GUIStyle(GUI.skin.box);panel.normal.background=Texture2D.whiteTexture;
        }
        private void OnGUI() {
            if(Session==null) return;
            if(text==null) CreateStyles();
            scale=HudScale();marginX=(Screen.width-HudWidth*scale)/2;marginY=(Screen.height-HudHeight*scale)/2;
            var previous=GUI.matrix;GUI.matrix=Matrix4x4.TRS(new Vector3(marginX,marginY,0),Quaternion.identity,new Vector3(scale,scale,1));
            if(!Session.Connected) ConnectScreen();
            else {
                var state=Session.State;
                if(state.phase==Phase.Lobby) Lobby(state);
                else if(state.phase==Phase.Results) Results(state);
                else { Hud(state); if(state.phase==Phase.Countdown) { Box(new Rect(560,326,320,140),panelBlue);Label(new Rect(560,339,320,48),"准备开饭",center);Label(new Rect(560,389,320,58),Mathf.CeilToInt(state.countdown).ToString(),number); } }
                if(Session.World.PauseOverlay) Pause();
            }
            GUI.matrix=previous;
        }
        private void Box(Rect rect,Color color) { var old=GUI.backgroundColor;GUI.backgroundColor=color;GUI.Box(rect,GUIContent.none,panel);GUI.backgroundColor=old; }
        private void Label(Rect rect,string value,GUIStyle style=null) { GUI.Label(rect,value,style??text); }
        private bool Button(Rect rect,string label,bool active=true) {
            bool old=GUI.enabled;GUI.enabled=old&&active;var bg=GUI.backgroundColor;GUI.backgroundColor=blue;
            bool pressed=GUI.Button(rect,label,button);GUI.enabled=old;GUI.backgroundColor=bg;return pressed;
        }
        private void ConnectScreen() {
            Curtain();
            Box(new Rect(300,100,840,710),panelBlue);
            Label(new Rect(350,135,740,60),"Kitchen Chaos 真人联机",title);
            Label(new Rect(350,205,740,50),"同一厨房 · 共同订单 · 300秒协作",heading);
            Label(new Rect(350,280,140,36),"厨师昵称");
            Session.Nickname=GUI.TextField(new Rect(505,275,550,46),Session.Nickname,16,field);
            Label(new Rect(350,342,140,36),"端口");
            Session.PortText=GUI.TextField(new Rect(505,337,150,46),Session.PortText,5,field);
            if(Button(new Rect(690,337,365,46),"创建房间",!Session.Connecting)) Session.HostRoom();
            Label(new Rect(350,418,140,36),"房主地址");
            Session.Address=GUI.TextField(new Rect(505,413,550,46),Session.Address,128,field);
            Label(new Rect(350,480,140,36),"房间码");
            Session.RoomCode=GUI.TextField(new Rect(505,475,150,46),Session.RoomCode,6,field).ToUpperInvariant();
            if(Button(new Rect(690,475,365,46),Session.Connecting?"正在连接…":"加入房间",!Session.Connecting)) Session.JoinRoom();
            Label(new Rect(350,552,720,65),Session.Notice,small);
            Label(new Rect(350,625,720,65),"支持单人及2–4人合作。局域网填写房主IPv4；同机双开填写127.0.0.1。\n创建后需把地址、端口、房间码一起告诉队友。",small);
            if(Button(new Rect(350,725,220,45),"返回主菜单")) Session.BackToMenu();
            if(Session.Connecting && Button(new Rect(835,725,220,45),"取消连接")) Session.Leave();
        }
        private void Lobby(Snapshot state) {
            Curtain();Box(new Rect(260,80,920,740),panelBlue);
            Label(new Rect(310,115,800,55),"厨房房间  "+state.code,title);
            string addresses=Session.IsHost?LocalAddresses():Session.Address;
            Label(new Rect(310,190,800,56),"房主地址 "+addresses+"    端口 "+Session.PortText,small);
            Label(new Rect(310,245,800,35),"成员 "+state.players.Length+" / "+Session.Config.maxPlayers+"    1人也可开局 · 全员准备后由房主开始",heading);
            int row=0;
            foreach(var player in state.players) {
                float y=305+row++*70;Box(new Rect(310,y,800,58),blue);
                Box(new Rect(326,y+16,24,24),NetworkWorldView.ChefColors[player.slot%4]);
                Label(new Rect(370,y+12,475,40),player.name+(player.id==state.host?"  房主":"")+(player.id==Session.LocalPlayer?"  你":""));
                Label(new Rect(900,y+12,185,40),player.ready?"✓ 已准备":"等待准备");
            }
            var me=state.players.FirstOrDefault(p=>p.id==Session.LocalPlayer);
            if(me!=null && Button(new Rect(310,620,240,55),me.ready?"取消准备":"准备")) Session.Request("ready",ready:!me.ready);
            if(Session.IsHost && Button(new Rect(575,620,240,55),"开始本局",state.players.Length>=Session.Config.minPlayers && state.players.All(p=>p.ready))) Session.Request("start");
            if(Button(new Rect(870,620,240,55),"退出房间")) Session.Leave();
            Label(new Rect(310,700,800,70),Session.Notice+"\n加入或离开房间后需重新准备。",small);
        }
        private static string LocalAddresses() {
            try { return string.Join(" / ",Dns.GetHostAddresses(Dns.GetHostName()).Where(a=>a.AddressFamily==AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)).Select(a=>a.ToString()).ToArray()); }
            catch { return "在命令提示符输入 ipconfig 查看IPv4"; }
        }
        private void Curtain() {
            Box(new Rect(Left,Top,Right-Left,Bottom-Top),new Color(.025f,.06f,.08f,.78f));
        }
        private void Hud(Snapshot state) {
            resultStarted=-1;
            DrawWorldLabels(state);
            var viewport=MatchViewport();
            float topEnd=(Screen.height*(1-viewport.y-viewport.height)-marginY)/scale;
            float bottomStart=(Screen.height*(1-viewport.y)-marginY)/scale;
            Box(new Rect(Left,Top,Right-Left,topEnd-Top),strip);
            Box(new Rect(Left,bottomStart,Right-Left,Bottom-bottomStart),strip);
            Box(new Rect(Left,topEnd-2,Right-Left,2),edge);
            Box(new Rect(Left,bottomStart,Right-Left,2),edge);
            DrawOrders(state);

            if(Button(new Rect(Right-487,Top+18,154,40),showHints?"隐藏提示 [H]":"显示提示 [H]")) showHints=!showHints;
            if(Button(new Rect(Right-323,Top+18,154,40),showMenu?"关闭菜单":"查看本局菜单")) showMenu=!showMenu;
            if(Button(new Rect(Right-159,Top+18,137,40),"设置 / 退出")) Session.World.PauseOverlay=true;
            Label(new Rect(Right-480,Top+66,452,25),"房间 "+state.code+"    完成 "+state.summary.completed+"    误交 "+state.summary.wrong,small);

            float scoreX=Left+20, clockX=Right-212, y=Bottom-72;
            Box(new Rect(scoreX,y,205,58),blue);
            Box(new Rect(scoreX,y,6,58),orange);
            Label(new Rect(scoreX+15,y+4,100,24),"团队收入",small);
            Label(new Rect(scoreX+16,y+24,145,34),state.summary.score+" 元",heading);
            bool urgent=state.remaining<=30;
            Box(new Rect(clockX,y,192,58),urgent?warning:blue);
            int seconds=Mathf.Max(0,Mathf.CeilToInt(state.remaining));
            Label(new Rect(clockX,y,192,43),string.Format("{0:00}:{1:00}",seconds/60,seconds%60),number);
            Box(new Rect(clockX+9,y+47,174,5),strip);
            Box(new Rect(clockX+9,y+47,174*Mathf.Clamp01(state.remaining/Session.Config.roundSeconds),5),urgent?orange:green);

            bool hasFeedback=Session.World.FeedbackUntil>Time.unscaledTime;
            if(showHints) DrawHints(state,hasFeedback);
            else if(hasFeedback) Label(new Rect(Left+246,Bottom-66,Right-Left-488,46),Session.World.Feedback,center);
            if(showMenu) DrawRecipeMenu(state);
        }
        private void DrawOrders(Snapshot state) {
            int row=0;
            foreach(var order in state.orders) {
                var recipe=state.menu[order.recipe];
                float x=Left+20+row++*218,y=Top+11;
                Box(new Rect(x,y,208,103),blue);
                Box(new Rect(x,y,208,4),green);
                Label(new Rect(x+9,y+8,190,25),recipe.name,orderTitle);
                Label(new Rect(x+9,y+32,190,22),"#"+order.id+"    "+recipe.reward+" 元",small);
                Ingredients(new Rect(x+9,y+55,190,45),recipe.ingredients,28,true,true);
            }
            if(row==0) Label(new Rect(Left+20,Top+20,330,35),"等待新订单…",heading);
        }
        private void DrawHints(Snapshot state,bool hasFeedback) {
            var selected=state.stations.FirstOrDefault(s=>s.id==Session.World.SelectedStation);
            var local=state.players.FirstOrDefault(p=>p.id==Session.LocalPlayer);
            var item=local==null || local.hand==0?null:state.items.FirstOrDefault(i=>i.id==local.hand);
            float x=Left+244,width=Right-Left-474,y=Bottom-79;
            string target=selected==null?"靠近并面向操作台":"目标："+StationName(selected);
            Label(new Rect(x,y,width,24),target+(item==null?"":"   ·   手持："+ItemName(item.kind)),small);
            if(item!=null && item.kind=="Plate") Ingredients(new Rect(x+370,y,Mathf.Min(350,width-375),24),item.ingredients,20,false,false);
            Label(new Rect(x,y+26,width,24),"WASD 移动    Shift 奔跑    E 交互    F 切菜    H 隐藏提示    Esc 菜单",small);
            Label(new Rect(x,y+51,width,24),hasFeedback?Session.World.Feedback:Session.Notice,small);
        }
        private void DrawRecipeMenu(Snapshot state) {
            Box(new Rect(330,150,780,540),panelBlue);
            Box(new Rect(330,150,780,5),edge);
            Label(new Rect(356,170,730,42),"本局菜单   按份数装盘，顺序不限",heading);
            for(int i=0;i<state.menu.Length;i++) {
                var recipe=state.menu[i];float y=228+i*108;
                Label(new Rect(356,y,730,32),recipe.name+"    "+recipe.reward+" 元");
                Ingredients(new Rect(356,y+35,710,61),recipe.ingredients,36,true,true);
            }
        }
        private void Ingredients(Rect rect,string[] ingredients,float iconSize,bool numbers,bool cookGuide) {
            if(ingredients.Length==0) { Label(rect,"空餐盘",small);return; }
            var groups=ingredients.GroupBy(v=>v).ToArray();
            float slot=Mathf.Min(56,rect.width/groups.Length);
            for(int i=0;i<groups.Length;i++) {
                float x=rect.x+i*slot;
                float size=Mathf.Min(iconSize,slot-5);
                float height=size+(numbers?12:0);
                Box(new Rect(x,rect.y,slot-3,height),tile);
                bool cook=cookGuide && groups[i].Key=="MeatPattyCooked";
                DrawIngredientIcon(new Rect(x+(slot-3-size)/2,rect.y,size,size),cook?Beef:groups[i].Key);
                if(numbers) Label(new Rect(x,rect.y+size,slot-3,12),cook?"煎×"+groups[i].Count():"×"+groups[i].Count(),tileNumber);
            }
        }
        private void DrawIngredientIcon(Rect rect,string ingredient) {
            KitchenObjectSO asset;
            if(!Session.World.Assets.TryGetValue(ingredient,out asset) || asset.sprite==null) return;
            var sprite=asset.sprite;var uv=sprite.textureRect;
            uv=new Rect(uv.x/sprite.texture.width,uv.y/sprite.texture.height,uv.width/sprite.texture.width,uv.height/sprite.texture.height);
            GUI.DrawTextureWithTexCoords(rect,sprite.texture,uv);
        }
        private void DrawWorldLabels(Snapshot state) {
            var cam=Camera.main;if(cam==null) return;
            if(showHints) foreach(var p in state.players) {
                var pos=cam.WorldToScreenPoint(Session.World.ChefPosition(p.id)+Vector3.up*2.4f);if(pos.z<0) continue;
                float x=(pos.x-marginX)/scale,y=(Screen.height-pos.y-marginY)/scale;
                Box(new Rect(x-58,y-14,116,27),new Color(.11f,.24f,.31f,.88f));
                Label(new Rect(x-54,y-13,109,25),p.name+(p.id==Session.LocalPlayer?" 你":""),small);
            }
            foreach(var s in state.stations) {
                bool beef=s.kind==StationKind.Supply && s.supply==Beef;
                bool highlight=s.id==Session.World.SelectedStation;
                var item=state.items.FirstOrDefault(i=>i.id==s.item);
                bool processing=(s.kind==StationKind.Cutting||s.kind==StationKind.Stove)&&item!=null;
                bool label=showHints && (highlight || processing || s.kind==StationKind.Plates);
                if(!beef && !processing && !label) continue;
                var pos=cam.WorldToScreenPoint(Session.World.Counters[s.id].GetKitchenObjectFollowTransform().position+Vector3.up*.6f);
                if(pos.z<0) continue;
                float x=(pos.x-marginX)/scale,y=(Screen.height-pos.y-marginY)/scale;
                if(beef) {
                    Box(new Rect(x-45,y-15,90,28),beefRed);
                    DrawIngredientIcon(new Rect(x-42,y-13,26,24),Beef);
                    Label(new Rect(x-14,y-14,60,27),highlight && showHints?"牛肉 [E]":"牛肉",small);
                } else if(label) {
                    Box(new Rect(x-61,y-14,122,27),highlight?blue:panelBlue);
                    string name=s.kind==StationKind.Plates?"餐盘 "+s.plateStock:highlight?StationName(s):ItemName(item.kind);
                    Label(new Rect(x-55,y-13,110,25),name,small);
                }
                if(processing) {
                    float width=label?110:74,height=label?8:6,progressY=label?y+16:y;
                    Box(new Rect(x-width/2,progressY,width,height),strip);
                    Box(new Rect(x-width/2,progressY,width*s.progress,height),item.kind=="MeatPattyCooked"?orange:green);
                }
            }
        }
        private void Results(Snapshot state) {
            if(resultStarted<0) resultStarted=Time.unscaledTime;
            Curtain();Box(new Rect(330,90,780,710),panelBlue);
            Label(new Rect(380,120,680,65),"本局厨房成绩",title);
            int stars=Mathf.Min(state.summary.stars,Mathf.FloorToInt((Time.unscaledTime-resultStarted)*3)+1);
            Label(new Rect(380,200,680,65),new string('★',stars)+new string('☆',5-stars),title);
            var r=state.summary;
            Label(new Rect(380,285,680,60),"团队实收  "+r.score+" 元    "+r.stars+" / 5 星",heading);
            Label(new Rect(380,365,680,240),"出餐收入："+r.earned+" 元\n完成订单："+r.completed+"\n失败订单："+(r.wrong+r.expired)+"（误交 "+r.wrong+"，到时未完成 "+r.expired+"）"+
                "\n参与玩家："+r.participants+" 人\n出餐正确率："+(r.accuracy*100).ToString("F1")+"%\n最佳出餐时间："+(r.completed>0?r.bestSeconds.ToString("F1")+" 秒":"暂无"),text);
            Label(new Rect(380,610,680,60),"出餐时间从该订单出现时计算。全局时间结束时，未完成订单统一扣款。",small);
            if(Session.IsHost && Button(new Rect(380,700,310,55),"返回房间再来一局")) Session.Request("return");
            if(!Session.IsHost) Label(new Rect(380,700,310,55),"等待房主返回房间…",small);
            if(Button(new Rect(740,700,310,55),"退出房间")) Session.Leave();
        }
        private void Pause() {
            Curtain();Box(new Rect(455,245,530,400),panelBlue);
            Label(new Rect(500,280,440,55),"联机菜单",title);
            Label(new Rect(500,355,440,90),"打开菜单时本机停止输入，房间时间继续流逝。\n"+Session.NetworkInfo,text);
            if(Button(new Rect(500,450,440,55),"继续协作")) Session.World.PauseOverlay=false;
            if(Button(new Rect(500,535,440,55),Session.IsHost?"关闭房间并退出":"退出房间")) Session.Leave();
        }
        public static string ItemName(string kind) {
            switch(kind) {
                case "Tomato":return "番茄";case "TomatoSlices":return "番茄片";case "Cabbage":return "卷心菜";
                case "CabbageSlices":return "卷心菜丝";case "Bread":return "面包";case "CheeseBlock":return "奶酪";
                case "CheeseSlices":return "奶酪片";case "MeatPattyUncooked":return "生牛肉饼";case "MeatPattyCooked":return "熟牛肉饼";
                case "MeatPattyBurned":return "焦牛肉饼";case "Plate":return "餐盘";default:return kind;
            }
        }
        private static string StationName(StationState s) {
            switch(s.kind) {
                case StationKind.Supply:return s.supply==Beef?"牛肉取料台":ItemName(s.supply)+"食材篮";case StationKind.Cutting:return "切菜砧板";
                case StationKind.Stove:return "烹饪炉灶";case StationKind.Plates:return "餐盘架";case StationKind.Delivery:return "出餐窗口";
                case StationKind.Trash:return "垃圾桶";default:return "空操作台";
            }
        }
    }
}
