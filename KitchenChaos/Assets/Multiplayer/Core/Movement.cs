using System;
using System.Collections.Generic;
using System.Linq;

namespace KitchenChaos.Multiplayer {
    [Serializable] public struct InputFrame {
        public int epoch;
        public long sequence;
        public float x,z;
        public bool run;
    }
    [Serializable] public struct MotionState {
        public int id;
        public long acknowledged;
        public float x,z,facingX,facingZ;
        public bool walking,running;
        public static MotionState From(PlayerState p) {
            return new MotionState{id=p.id,acknowledged=p.lastInput,x=p.x,z=p.z,facingX=p.facingX,
                facingZ=p.facingZ,walking=p.walking,running=p.running};
        }
        public void Apply(PlayerState p) {
            p.x=x;p.z=z;p.facingX=facingX;p.facingZ=facingZ;p.walking=walking;p.running=running;p.lastInput=acknowledged;
        }
    }
    public static class Movement {
        public const float TickSeconds=1f/30f;
        public static void Advance(PlayerState p,float x,float z,bool run,float dt,Rules rules,
            Func<PlayerState,float,float,float[]> resolve,IEnumerable<PlayerState> players) {
            if(float.IsNaN(x)||float.IsInfinity(x)||float.IsNaN(z)||float.IsInfinity(z)) { x=z=0; }
            float length=(float)Math.Sqrt(x*x+z*z);
            if(length>1) { x/=length;z/=length; }
            p.walking=x*x+z*z>.001f;p.running=p.walking&&run;
            if(!p.walking) return;
            p.facingX=x;p.facingZ=z;
            float speed=(p.running?rules.runSpeed:rules.walkSpeed)*dt;
            float nx=p.x+x*speed,nz=p.z+z*speed;
            if(resolve!=null) { var result=resolve(p,nx,nz);nx=result[0];nz=result[1]; }
            if(players!=null && players.Any(other=>other.id!=p.id &&
                (nx-other.x)*(nx-other.x)+(nz-other.z)*(nz-other.z)<.81f)) return;
            p.x=nx;p.z=nz;
        }
    }
    // Prediction owns only movement. Hands, ingredients and money always come from the host.
    public sealed class MovementPrediction {
        public PlayerState Position { get; private set; }
        public long Sequence { get; private set; }
        public int Epoch { get; private set; }
        public float LastCorrection { get; private set; }
        public int PendingCount { get { return pending.Count; } }
        private readonly List<InputFrame> pending=new List<InputFrame>();
        private readonly Queue<InputFrame> recent=new Queue<InputFrame>();
        private long acknowledged;
        public void Reset(PlayerState p,int epoch) {
            Position=new PlayerState{id=p.id,x=p.x,z=p.z,facingX=p.facingX,facingZ=p.facingZ};
            Epoch=epoch;Sequence=acknowledged=p.lastInput;pending.Clear();recent.Clear();LastCorrection=0;
        }
        public InputFrame Advance(float x,float z,bool run,Rules rules,Func<PlayerState,float,float,float[]> resolve,IEnumerable<PlayerState> others) {
            if(Position==null) throw new InvalidOperationException("Prediction is not initialized");
            var frame=new InputFrame{epoch=Epoch,sequence=++Sequence,x=x,z=z,run=run};
            pending.Add(frame);recent.Enqueue(frame);while(recent.Count>8) recent.Dequeue();
            // Bounded history: if the host disappears, do not accumulate unlimited replay work.
            if(pending.Count>180) pending.RemoveAt(0);
            Movement.Advance(Position,x,z,run,Movement.TickSeconds,rules,resolve,others);
            return frame;
        }
        public InputFrame[] Recent() { return recent.ToArray(); }
        public void Reconcile(PlayerState p,Rules rules,Func<PlayerState,float,float,float[]> resolve,IEnumerable<PlayerState> others) {
            if(Position==null || p.id!=Position.id || p.lastInput<acknowledged) return;
            acknowledged=p.lastInput;pending.RemoveAll(f=>f.sequence<=acknowledged);
            float oldX=Position.x,oldZ=Position.z;
            MotionState.From(p).Apply(Position);
            foreach(var f in pending) Movement.Advance(Position,f.x,f.z,f.run,Movement.TickSeconds,rules,resolve,others);
            LastCorrection=(float)Math.Sqrt((oldX-Position.x)*(oldX-Position.x)+(oldZ-Position.z)*(oldZ-Position.z));
        }
    }
}
