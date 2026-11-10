using System;

namespace KitchenChaos.Multiplayer {
    public enum Phase { Lobby, Countdown, Playing, Results }
    public enum StationKind { Supply, Clear, Cutting, Stove, Plates, Delivery, Trash }

    // Fields (rather than properties) keep these DTOs compatible with Unity JsonUtility.
    [Serializable] public class Packet {
        public string type;
        public int protocol = 1;
        public string contentHash, name, code, action, reason;
        public long seq;
        public int epoch, target, expectedVersion, player;
        public float x, z;
        public bool run, ready, ok;
        public Snapshot snapshot;
    }
    [Serializable] public class PlayerState {
        public int id, hand, slot;
        public string name;
        public bool ready, walking, running;
        public float x, z, facingX, facingZ = 1;
        [NonSerialized] public float inputX, inputZ, inputAge, cutCooldown;
        [NonSerialized] public float spawnX, spawnZ;
        [NonSerialized] public bool inputRun;
    }
    [Serializable] public class ItemState {
        public int id;
        public string kind, owner;
        public string[] ingredients = new string[0];
    }
    [Serializable] public class StationState {
        public int id, item, version, cuts, plateStock = 4;
        public string key, supply;
        public StationKind kind;
        public float x, z, progress, elapsed, plateTimer;
    }
    [Serializable] public class Recipe {
        public string name;
        public string[] ingredients;
    }
    [Serializable] public class Order {
        public int id, recipe;
        public float createdAt;
    }
    [Serializable] public class Summary {
        public int completed, wrong, expired, participants, score, stars;
        public float accuracy, bestSeconds;
    }
    [Serializable] public class Snapshot {
        public long revision;
        public int epoch, host, lastActor, lastStation;
        public string code, lastEvent;
        public long eventId;
        public Phase phase;
        public float remaining, countdown;
        public PlayerState[] players;
        public StationState[] stations;
        public ItemState[] items;
        public Recipe[] menu;
        public Order[] orders;
        public Summary summary;
    }
    [Serializable] public class Rules {
        public float roundSeconds = 300, orderInterval = 4, countdownSeconds = 3;
        public int maxOrders = 4, maxPlayers = 4, minPlayers = 2;
        public float walkSpeed = 7, runSpeed = 10.5f, interactDistance = 2.5f;
        public float cutInterval = 1f, frySeconds = 8, burnSeconds = 8, plateInterval = 4;
        public int cutCount = 4, successPoints = 100, wrongPenalty = 10, timeoutPenalty = 10;
        public int[] starThresholds = { 100, 300, 600, 1000 };
        public Recipe[] recipes = new Recipe[0];
        public void Validate() {
            if (roundSeconds < 5 || roundSeconds > 3600 || orderInterval < .5f || maxOrders != 4 ||
                maxPlayers < 2 || maxPlayers > 4 || minPlayers < 1 || minPlayers > maxPlayers ||
                countdownSeconds < 0 || walkSpeed <= 0 || runSpeed < walkSpeed || interactDistance < 1 ||
                cutCount < 1 || cutInterval < .1f || frySeconds < 1 || burnSeconds < 1 || plateInterval < .5f ||
                successPoints <= 0 || wrongPenalty < 0 || timeoutPenalty < 0 ||
                starThresholds == null || starThresholds.Length != 4 || recipes == null || recipes.Length < 4)
                throw new ArgumentException("联机玩法配置无效，请恢复 Resources/MultiplayerRules.json");
            for (int i = 0; i < 4; i++)
                if (starThresholds[i] <= 0 || (i > 0 && starThresholds[i] <= starThresholds[i-1]))
                    throw new ArgumentException("星级阈值必须为四个递增正数");
            foreach (var r in recipes) {
                if (r == null || string.IsNullOrEmpty(r.name) || r.ingredients == null || r.ingredients.Length < 1 || r.ingredients.Length > 8)
                    throw new ArgumentException("食谱名称或数量无效");
                foreach (var ingredient in r.ingredients)
                    if (!KitchenServer.IsPlatable(ingredient)) throw new ArgumentException("食谱包含无法装盘的食材: " + ingredient);
            }
        }
    }
}
