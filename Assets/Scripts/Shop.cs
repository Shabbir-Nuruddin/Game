using System.Collections.Generic;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// THE CRYPT SHOP — where blood shards go. Cosmetics ONLY (the castle takes
    /// shards, never skill): purchasable skins (defined in Skins.cs with a price),
    /// death effects layered onto the gore, movement trails, and gravestone taunts
    /// that ride your death echo into other players' games. Ownership is an
    /// `own_&lt;id&gt;` pref; equipped picks mirror ti_skin (`ti_deathfx` / `ti_trail` /
    /// `ti_taunt`). Purchases go through Currency.Spend so the balance can never
    /// go negative.
    /// </summary>
    public static class Shop
    {
        public class Item
        {
            public string id, name, desc, kind;   // kind: "deathfx" | "trail" | "taunt"
            public int price;
            public Color tint = Color.white;
        }

        // Non-skin catalog. Purchasable SKINS live in Skins.All (price > 0) so the
        // Wardrobe stays the single source of truth for looks; the shop lists both.
        public static readonly List<Item> All = new()
        {
            // Death effects — your demise, but fancier (layered onto the gore).
            new Item { id = "deathfx_gold", kind = "deathfx", name = "Gilded Demise", price = 150,
                       desc = "die rich: a burst of gold", tint = Theme.Coin },
            new Item { id = "deathfx_bats", kind = "deathfx", name = "Bat Burst", price = 200,
                       desc = "your soul scatters as bats", tint = new Color(0.45f, 0.2f, 0.6f) },
            new Item { id = "deathfx_ash", kind = "deathfx", name = "Crumble to Ash", price = 250,
                       desc = "slow grey ash, very dramatic", tint = new Color(0.65f, 0.63f, 0.6f) },
            // Trails — a wake that follows you while you move.
            new Item { id = "trail_blood", kind = "trail", name = "Blood Mist", price = 200,
                       desc = "a red mist follows you", tint = new Color(0.85f, 0.12f, 0.18f, 0.7f) },
            new Item { id = "trail_ember", kind = "trail", name = "Ember Wake", price = 250,
                       desc = "smoulder as you run", tint = new Color(1f, 0.55f, 0.15f, 0.7f) },
            new Item { id = "trail_ecto", kind = "trail", name = "Ectoplasm", price = 300,
                       desc = "leave a ghostly residue", tint = new Color(0.55f, 0.95f, 0.7f, 0.6f) },
            // Gravestone taunts — appended to YOUR tombstone in other players' games.
            new Item { id = "taunt_skill", kind = "taunt", name = "Skill Issue", price = 80,
                       desc = "your grave says: skill issue." },
            new Item { id = "taunt_easy", kind = "taunt", name = "Easy, btw", price = 80,
                       desc = "your grave says: this floor is easy, btw." },
            new Item { id = "taunt_meant", kind = "taunt", name = "All Part of the Plan", price = 80,
                       desc = "your grave says: I meant to do that." },
        };

        // The taunt LINE other players read (kept preset — no free text on the wire).
        static string TauntLine(string id) => id switch
        {
            "taunt_skill" => "skill issue.",
            "taunt_easy"  => "this floor is easy, btw.",
            "taunt_meant" => "I meant to do that.",
            _             => null,
        };

        public static bool Owns(string id) => PlayerPrefs.GetInt("own_" + id, 0) == 1;

        public static bool Buy(Item it)
        {
            if (it == null || Owns(it.id) || !Currency.Spend(it.price)) return false;
            PlayerPrefs.SetInt("own_" + it.id, 1);
            PlayerPrefs.Save();
            Analytics.Track("shop_purchase", new Dictionary<string, object>
            {
                { "item_id", it.id }, { "kind", it.kind },
                { "price", it.price }, { "balance", Currency.Balance },
            });
            return true;
        }

        /// <summary>Buy a purchasable skin (SkinDef with price > 0) by shard spend.</summary>
        public static bool BuySkin(SkinDef s)
        {
            if (s == null || s.price <= 0 || Skins.IsUnlocked(s) || !Currency.Spend(s.price)) return false;
            PlayerPrefs.SetInt("own_skin_" + s.id, 1);
            PlayerPrefs.Save();
            Analytics.Track("shop_purchase", new Dictionary<string, object>
            {
                { "item_id", "skin_" + s.id }, { "kind", "skin" },
                { "price", s.price }, { "balance", Currency.Balance },
            });
            return true;
        }

        // ---- equipped picks (one per kind; "" = none) ----
        public static string Equipped(string kind) => PlayerPrefs.GetString("ti_" + kind, "");
        public static void Equip(string kind, string id)
        {
            PlayerPrefs.SetString("ti_" + kind, id);
            PlayerPrefs.Save();
            Analytics.Track("shop_equip", new Dictionary<string, object> { { "item_id", id }, { "kind", kind } });
        }

        public static Item Get(string id) => All.Find(i => i.id == id);

        /// <summary>The cheapest thing you don't own yet (shop items + priced skins) —
        /// drives every "N more shards until X" teaser. Null when you own it all.</summary>
        public static object NextUnlock()
        {
            Item bestItem = null;
            foreach (var it in All)
                if (!Owns(it.id) && (bestItem == null || it.price < bestItem.price)) bestItem = it;
            SkinDef bestSkin = null;
            foreach (var s in Skins.All)
                if (s.price > 0 && !Skins.IsUnlocked(s) && (bestSkin == null || s.price < bestSkin.price)) bestSkin = s;
            if (bestItem == null) return bestSkin;
            if (bestSkin == null) return bestItem;
            return bestSkin.price < bestItem.price ? (object)bestSkin : bestItem;
        }

        public static string UnlockName(object o) => o is SkinDef s ? s.name : (o as Item)?.name;
        public static int UnlockPrice(object o) => o is SkinDef s ? s.price : (o as Item)?.price ?? 0;

        /// <summary>Suffix the equipped taunt onto a death-echo cause so YOUR grave
        /// talks trash in other players' games. Empty when nothing is equipped.</summary>
        public static string TauntSuffix()
        {
            string line = TauntLine(Equipped("taunt"));
            return string.IsNullOrEmpty(line) ? "" : "  …" + line;
        }

        /// <summary>The equipped death effect, layered on top of the standard gore.</summary>
        public static void PlayDeathFx(Vector3 pos)
        {
            switch (Equipped("deathfx"))
            {
                case "deathfx_gold":
                    Fx.Burst(pos, Theme.Coin, 16, 6.5f, 0.16f, 0.6f, 10f);
                    Fx.Ring(pos, new Color(1f, 0.85f, 0.4f, 0.8f), 2.6f, 0.4f);
                    break;
                case "deathfx_bats":
                    // "bats": small dark bits that flap UPWARD (negative gravity).
                    Fx.Burst(pos, new Color(0.25f, 0.12f, 0.3f, 1f), 8, 4.5f, 0.22f, 0.7f, -9f);
                    Fx.Ring(pos, new Color(0.6f, 0.25f, 0.8f, 0.7f), 2.4f, 0.45f);
                    break;
                case "deathfx_ash":
                    Fx.Burst(pos, new Color(0.62f, 0.6f, 0.58f, 0.9f), 18, 2.6f, 0.14f, 1.1f, 2.5f);
                    Fx.Ring(pos, new Color(0.7f, 0.68f, 0.65f, 0.5f), 3.2f, 0.7f);
                    break;
            }
        }

        /// <summary>Attach the equipped movement trail to the freshly spawned player.</summary>
        public static void AttachTrail(GameObject player)
        {
            var it = Get(Equipped("trail"));
            if (it == null || it.kind != "trail" || !Owns(it.id)) return;
            player.AddComponent<CosmeticTrail>().tint = it.tint;
        }
    }

    /// <summary>A cosmetic wake: while the owner moves fast enough, drop small
    /// fading bits behind them. Pure Fx — no colliders, no gameplay.</summary>
    public class CosmeticTrail : MonoBehaviour
    {
        public Color tint = Color.white;
        Rigidbody2D _rb;
        float _next;

        void Start() { _rb = GetComponent<Rigidbody2D>(); }

        void Update()
        {
            if (_rb == null || _rb.linearVelocity.magnitude < 2f) return;
            _next -= Time.deltaTime;
            if (_next > 0f) return;
            _next = 0.06f;
            var go = Theme.Box("TrailBit", null, transform.position + (Vector3)(Random.insideUnitCircle * 0.15f),
                new Vector2(0.14f, 0.14f), tint, 4);
            go.AddComponent<FxBit>().Init(Vector2.zero, 0.45f, -1.5f);   // drift up as it fades
        }
    }

    /// <summary>A floating "+N" gold TextMesh that drifts up and fades — the
    /// world-space shard pop. Unscaled time (deaths freeze-frame the game).</summary>
    public class ShardFloater : MonoBehaviour
    {
        TextMesh _tm;
        float _t;
        const float Life = 0.8f;

        public static void Spawn(Vector3 pos, int amount)
        {
            var go = new GameObject("ShardFloater");
            // Parent to nothing persistent-scene-wise; it self-destructs fast and the
            // level rebuild on death can't eat it (it isn't under the level root).
            Object.DontDestroyOnLoad(go);
            go.transform.position = pos + Vector3.up * 0.9f;
            var tm = go.AddComponent<TextMesh>();
            tm.text = "+" + amount;
            tm.fontSize = 56; tm.characterSize = 0.06f; tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.LowerCenter; tm.alignment = TextAlignment.Center;
            tm.color = Theme.Coin;
            go.GetComponent<MeshRenderer>().sortingOrder = 20;
            go.AddComponent<ShardFloater>()._tm = tm;
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            transform.position += Vector3.up * (1.1f * Time.unscaledDeltaTime);
            if (_tm != null)
            {
                var c = _tm.color; c.a = 1f - Mathf.Clamp01(_t / Life); _tm.color = c;
            }
            if (_t >= Life) Destroy(gameObject);
        }
    }
}
