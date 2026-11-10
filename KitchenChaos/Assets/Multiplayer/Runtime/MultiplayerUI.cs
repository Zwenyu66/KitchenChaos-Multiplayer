using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace KitchenChaos.Multiplayer {
    public sealed class MultiplayerUI : MonoBehaviour {
        public MultiplayerSession Session;
        private GUIStyle title,heading,text,small,button,field,panel,center;
        private readonly Color cream=new Color(.98f,.97f,.94f),ink=new Color(.12f,.17f,.2f),orange=new Color(1,.42f,.21f),green=new Color(.22f,.66f,.52f);
        private float scale,marginX,marginY,resultStarted=-1;
        private bool showMenu;
        private void CreateStyles() {
            Font font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Noto Sans CJK SC","SimHei","Arial"},20);
            text=new GUIStyle(GUI.skin.label){font=font,fontSize=19,wordWrap=true};text.normal.textColor=ink;
            small=new GUIStyle(text){fontSize=15};
            title=new GUIStyle(text){fontSize=38,fontStyle=FontStyle.Bold};
            heading=new GUIStyle(text){fontSize=25,fontStyle=FontStyle.Bold};
            center=new GUIStyle(heading){alignment=TextAnchor.MiddleCenter};
            button=new GUIStyle(GUI.skin.button){font=font,fontSize=20,padding=new RectOffset(12,12,8,8)};
            field=new GUIStyle(GUI.skin.textField){font=font,fontSize=22,padding=new RectOffset(12,10,8,8)};
            panel=new GUIStyle(GUI.skin.box);panel.normal.background=Texture2D.whiteTexture;
        }
        private void OnGUI() {
            if(Session==null) return;
            if(text==null) CreateStyles();
            scale=Mathf.Min(Screen.width/1440f,Screen.height/900f);marginX=(Screen.width-1440*scale)/2;marginY=(Screen.height-900*scale)/2;
            var previous=GUI.matrix;GUI.matrix=Matrix4x4.TRS(new Vector3(marginX,marginY,0),Quaternion.identity,new Vector3(scale,scale,1));
            if(!Session.Connected) ConnectScreen();
            else {
                var state=Session.State;
                if(state.phase==Phase.Lobby) Lobby(state);
                else if(state.phase==Phase.Results) Results(state);
                else { Hud(state); if(state.phase==Phase.Countdown) { Box(new Rect(520,290,400,180),cream);Label(new Rect(520,310,400,70),"准备开饭",center);Label(new Rect(520,380,400,70),Mathf.CeilToInt(state.countdown).ToString(),center); } }
                if(Session.World.PauseOverlay) Pause();
            }
            GUI.matrix=previous;
        }
        private void Box(Rect rect,Color color) { var old=GUI.backgroundColor;GUI.backgroundColor=color;GUI.Box(rect,GUIContent.none,panel);GUI.backgroundColor=old; }
        private void Label(Rect rect,string value,GUIStyle style=null) { GUI.Label(rect,value,style??text); }
        private bool Button(Rect rect,string label,bool active=true) {
            bool old=GUI.enabled;GUI.enabled=active;var bg=GUI.backgroundColor;GUI.backgroundColor=orange;
            bool pressed=GUI.Button(rect,label,button);GUI.enabled=old;GUI.backgroundColor=bg;return pressed;
        }
        private void ConnectScreen() {
            Box(new Rect(0,0,1440,900),new Color(.08f,.12f,.14f,.65f));
            Box(new Rect(300,100,840,710),cream);
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
            Label(new Rect(350,625,720,65),"两人至四人合作。局域网填写房主IPv4；同机双开填写127.0.0.1。\n创建后需把地址、端口、房间码一起告诉队友。",small);
            if(Button(new Rect(350,725,220,45),"返回主菜单")) Session.BackToMenu();
            if(Session.Connecting && Button(new Rect(835,725,220,45),"取消连接")) Session.Leave();
        }
        private void Lobby(Snapshot state) {
            Box(new Rect(0,0,1440,900),new Color(.08f,.12f,.14f,.65f));Box(new Rect(260,80,920,740),cream);
            Label(new Rect(310,115,800,55),"厨房房间  "+state.code,title);
            string addresses=Session.IsHost?LocalAddresses():Session.Address;
            Label(new Rect(310,190,800,56),"房主地址 "+addresses+"    端口 "+Session.PortText,small);
            Label(new Rect(310,245,800,35),"成员 "+state.players.Length+" / "+Session.Config.maxPlayers+"    全员准备后由房主开始",heading);
            int row=0;
            foreach(var player in state.players) {
                float y=305+row++*70;Box(new Rect(310,y,800,58),new Color(.91f,.92f,.9f));
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
        private void Hud(Snapshot state) {
            resultStarted=-1;
            DrawWorldLabels(state);
            Box(new Rect(25,20,275,135),cream);
            Label(new Rect(45,32,235,40),"团队得分  "+state.summary.score,heading);
            Label(new Rect(45,82,235,60),"完成 "+state.summary.completed+"    误交 "+state.summary.wrong+"\n房间 "+state.code,small);
            Box(new Rect(585,20,270,75),state.remaining<=30?new Color(1,.55f,.5f):cream);
            int seconds=Mathf.CeilToInt(state.remaining);
            Label(new Rect(590,24,260,65),string.Format("{0:00}:{1:00}",seconds/60,seconds%60),center);
            if(Button(new Rect(910,24,190,42),showMenu?"关闭菜单":"查看本局菜单")) showMenu=!showMenu;
            if(Button(new Rect(1220,24,190,42),"设置 / 退出")) Session.World.PauseOverlay=true;
            if(Session.World.FeedbackUntil>Time.unscaledTime) {
                float left=Session.World.FeedbackUntil-Time.unscaledTime;
                var feedbackStyle=new GUIStyle(center);feedbackStyle.normal.textColor=new Color(.13f,.6f,.34f,Mathf.Min(1,left));
                Label(new Rect(455,110-left*12,570,60),Session.World.Feedback,feedbackStyle);
            }
            int row=0;
            foreach(var order in state.orders) {
                var recipe=state.menu[order.recipe];float y=100+row++*128;
                Box(new Rect(1080,y,330,116),cream);Label(new Rect(1095,y+7,300,33),"#"+order.id+"  "+recipe.name,heading);
                Ingredients(new Rect(1095,y+45,300,65),recipe.ingredients);
            }
            var local=state.players.FirstOrDefault(p=>p.id==Session.LocalPlayer);
            Box(new Rect(25,725,1030,150),cream);
            var selected=state.stations.FirstOrDefault(s=>s.id==Session.World.SelectedStation);
            Label(new Rect(45,735,990,35),selected==null?"面向操作台并靠近后交互":"当前目标："+StationName(selected),heading);
            Label(new Rect(45,778,990,30),"WASD / 方向键移动    Shift 奔跑    E 拿取 / 放置 / 装盘 / 出餐    F 切菜    Esc 菜单",small);
            Label(new Rect(45,817,990,45),Session.Notice,small);
            if(local!=null && local.hand!=0) {
                var item=state.items.FirstOrDefault(i=>i.id==local.hand);
                if(item!=null) {
                    Box(new Rect(25,630,670,80),cream);Label(new Rect(42,637,180,38),"手持 "+ItemName(item.kind),small);
                    if(item.kind=="Plate") Ingredients(new Rect(210,638,460,62),item.ingredients);
                }
            }
            if(showMenu) {
                Box(new Rect(330,150,700,540),cream);Label(new Rect(355,170,650,42),"本局四道菜  请按从左到右的顺序装盘",heading);
                for(int i=0;i<state.menu.Length;i++) { float y=228+i*108;Label(new Rect(355,y,630,33),state.menu[i].name);Ingredients(new Rect(355,y+34,640,65),state.menu[i].ingredients); }
            }
        }
        private void Ingredients(Rect rect,string[] ingredients) {
            if(ingredients.Length==0) { Label(rect,"空餐盘",small);return; }
            float width=Mathf.Min(58,rect.width/ingredients.Length);
            for(int i=0;i<ingredients.Length;i++) {
                KitchenObjectSO asset;
                if(Session.World.Assets.TryGetValue(ingredients[i],out asset) && asset.sprite!=null) {
                    var sprite=asset.sprite;var uv=sprite.textureRect;
                    uv=new Rect(uv.x/sprite.texture.width,uv.y/sprite.texture.height,uv.width/sprite.texture.width,uv.height/sprite.texture.height);
                    GUI.DrawTextureWithTexCoords(new Rect(rect.x+i*width,rect.y,36,36),sprite.texture,uv);
                }
                Label(new Rect(rect.x+i*width,rect.y+36,width,26),(i+1).ToString(),small);
            }
        }
        private void DrawWorldLabels(Snapshot state) {
            var cam=Camera.main;if(cam==null) return;
            foreach(var p in state.players) {
                var pos=cam.WorldToScreenPoint(Session.World.ChefPosition(p.id)+Vector3.up*2.4f);if(pos.z<0) continue;
                float x=(pos.x-marginX)/scale,y=(Screen.height-pos.y-marginY)/scale;
                Box(new Rect(x-70,y-16,140,32),cream);Label(new Rect(x-66,y-15,132,30),p.name+(p.id==Session.LocalPlayer?" 你":""),small);
            }
            foreach(var s in state.stations) {
                bool highlight=s.id==Session.World.SelectedStation;
                var item=state.items.FirstOrDefault(i=>i.id==s.item);
                bool processing=(s.kind==StationKind.Cutting||s.kind==StationKind.Stove)&&item!=null;
                if(!highlight&&!processing&&s.kind!=StationKind.Plates) continue;
                var pos=cam.WorldToScreenPoint(Session.World.Counters[s.id].GetKitchenObjectFollowTransform().position+Vector3.up*.6f);
                if(pos.z<0) continue;float x=(pos.x-marginX)/scale,y=(Screen.height-pos.y-marginY)/scale;
                Box(new Rect(x-68,y-15,136,30),highlight?orange:cream);
                string label=s.kind==StationKind.Plates?"餐盘 "+s.plateStock:highlight?StationName(s):item==null?"":ItemName(item.kind);
                Label(new Rect(x-62,y-14,124,29),label,small);
                if(processing) {
                    Box(new Rect(x-60,y+18,120,9),new Color(.2f,.2f,.2f));
                    Box(new Rect(x-60,y+18,120*s.progress,9),item.kind=="MeatPattyCooked"?orange:green);
                }
            }
        }
        private void Results(Snapshot state) {
            if(resultStarted<0) resultStarted=Time.unscaledTime;
            Box(new Rect(0,0,1440,900),new Color(.08f,.12f,.14f,.65f));Box(new Rect(330,90,780,710),cream);
            Label(new Rect(380,120,680,65),"本局厨房成绩",title);
            int stars=Mathf.Min(state.summary.stars,Mathf.FloorToInt((Time.unscaledTime-resultStarted)*3)+1);
            Label(new Rect(380,200,680,65),new string('★',stars)+new string('☆',5-stars),title);
            var r=state.summary;
            Label(new Rect(380,285,680,60),"团队总分  "+r.score+"    "+r.stars+" / 5 星",heading);
            Label(new Rect(380,365,680,240),"完成订单："+r.completed+"\n失败订单："+(r.wrong+r.expired)+"（误交 "+r.wrong+"，到时未完成 "+r.expired+"）"+
                "\n参与玩家："+r.participants+" 人\n出餐正确率："+(r.accuracy*100).ToString("F1")+"%\n最佳出餐时间："+(r.completed>0?r.bestSeconds.ToString("F1")+" 秒":"暂无"),text);
            Label(new Rect(380,610,680,60),"出餐时间从该订单出现时计算。全局时间结束时，未完成订单统一扣分。",small);
            if(Session.IsHost && Button(new Rect(380,700,310,55),"返回房间再来一局")) Session.Request("return");
            if(!Session.IsHost) Label(new Rect(380,700,310,55),"等待房主返回房间…",small);
            if(Button(new Rect(740,700,310,55),"退出房间")) Session.Leave();
        }
        private void Pause() {
            Box(new Rect(0,0,1440,900),new Color(0,0,0,.65f));Box(new Rect(455,245,530,400),cream);
            Label(new Rect(500,280,440,55),"联机菜单",title);
            Label(new Rect(500,355,440,65),"打开菜单时本机停止输入，房间时间继续流逝。",text);
            if(Button(new Rect(500,450,440,55),"继续协作")) Session.World.PauseOverlay=false;
            if(Button(new Rect(500,535,440,55),Session.IsHost?"关闭房间并退出":"退出房间")) Session.Leave();
        }
        public static string ItemName(string kind) {
            switch(kind) {
                case "Tomato":return "番茄";case "TomatoSlices":return "番茄片";case "Cabbage":return "卷心菜";
                case "CabbageSlices":return "卷心菜丝";case "Bread":return "面包";case "CheeseBlock":return "奶酪";
                case "CheeseSlices":return "奶酪片";case "MeatPattyUncooked":return "生肉饼";case "MeatPattyCooked":return "熟肉饼";
                case "MeatPattyBurned":return "焦肉饼";case "Plate":return "餐盘";default:return kind;
            }
        }
        private static string StationName(StationState s) {
            switch(s.kind) {
                case StationKind.Supply:return ItemName(s.supply)+"食材篮";case StationKind.Cutting:return "切菜砧板";
                case StationKind.Stove:return "烹饪炉灶";case StationKind.Plates:return "餐盘架";case StationKind.Delivery:return "出餐窗口";
                case StationKind.Trash:return "垃圾桶";default:return "空操作台";
            }
        }
    }
}
