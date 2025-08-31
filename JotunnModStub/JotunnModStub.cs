using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

// Minimal Valheim plugin: draws red 2D arrows around the player's torso
// when nearby hostiles have the player targeted. No prefabs, no assets. // Tested conceptually for BepInEx 5 + Valheim API types. May need minor name tweaks if game API changes.
namespace JotunnModStub
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    //[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class JotunnModStub : BaseUnityPlugin
    {
        public const string PluginGUID = "com.example.valheim.dangerindicator";
        public const string PluginName = "DangerIndicator";
        public const string PluginVersion = "0.1.0";
        private Harmony _harmony;

        // Config
        private static ConfigEntry<bool> cfgEnabled;
        private static ConfigEntry<float> cfgScanRadius;
        private static ConfigEntry<float> cfgRefreshSec;
        private static ConfigEntry<float> cfgUiScale;
        private static ConfigEntry<float> cfgRingRadiusPx;
        private static ConfigEntry<float> cfgOpacity;

        // State
        private float _scanTimer = 0f;
        private readonly bool[] _dirActive = new bool[8]; // N, NE, E, SE, S, SW, W, NW
        private static Texture2D _triTex; // simple red right-pointing triangle we rotate as needed

        private void Awake()
        {
            cfgEnabled = Config.Bind("General", "Enabled", true, "Master switch");
            cfgScanRadius = Config.Bind("General", "ScanRadius", 50f, "Meters to scan for hostile AI targeting player");
            cfgRefreshSec = Config.Bind("General", "RefreshSeconds", 0.2f, "How often to rescan (seconds)");
            cfgUiScale = Config.Bind("UI", "ArrowSize", 18f, "Triangle size in pixels");
            cfgRingRadiusPx = Config.Bind("UI", "RingRadiusPx", 110f,
                "Distance from screen center (belly) to place arrows");
            cfgOpacity = Config.Bind("UI", "Opacity", 0.95f, "Arrow opacity 0..1");
            _harmony = new Harmony("com.example.valheim.dangerindicator");
            _harmony.PatchAll();
            if (_triTex == null) _triTex = MakeTriangleTexture(Color.red);
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        private void Update()
        {
            if (!cfgEnabled.Value) return;
            if (Player.m_localPlayer == null) return;
            _scanTimer -= Time.deltaTime;
            if (_scanTimer > 0f) return;
            _scanTimer = Mathf.Max(0.02f, cfgRefreshSec.Value);

            // Reset directions
            for (int i = 0; i < _dirActive.Length; i++) _dirActive[i] = false;
            var player = Player.m_localPlayer;
            Vector3 pPos = player.transform.position;
            Vector3 pFwd = player.transform.forward;

            // Find hostile AI with target = player within radius
            // We iterate over all BaseAI components present in scene
            var allAi = FindObjectsOfType<BaseAI>();
            float r2 = cfgScanRadius.Value * cfgScanRadius.Value;
            foreach (var ai in allAi)
            {
                if (!ai || !ai.m_character) continue;
                if (ai.m_character.IsDead()) continue;
                if (!ai.m_character.IsTamed())
                {
                    Character target = ai.GetTargetCreature();
                    if (!target) continue;
                    if (!ReferenceEquals(target, player)) continue;

                    // In range?
                    Vector3 ePos = ai.m_character.transform.position;
                    if ((ePos - pPos).sqrMagnitude > r2) continue;

                    // Direction from player to enemy on horizontal plane
                    Vector3 dir = (ePos - pPos);
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 0.01f) continue;
                    dir.Normalize();

                    // Angle between player's forward and dir
                    float angle = Vector3.SignedAngle(pFwd, dir, Vector3.up); // -180..180, 0=front
                    // Map to 8 sectors. Sector 0=N(front), 1=NE(45), 2=E(90), ..., 7=NW(315)
                    // Shift by 22.5 so boundaries fall mid-way
                    float shifted = angle + 22.5f;
                    if (shifted < 0f) shifted += 360f;
                    int sector = Mathf.FloorToInt((shifted % 360f) / 45f); // 0..7
                    if (sector < 0 || sector > 7) continue;
                    _dirActive[sector] = true;
                }
            }
        }

        private void OnGUI()
        {
            if (!cfgEnabled.Value) return;
            if (!Player.m_localPlayer) return;
            if (!_triTex) return;

            // Screen-space belly/torso approximate center: slightly below true center
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.58f);
            float r = cfgRingRadiusPx.Value;
            float size = cfgUiScale.Value;

            // Precompute positions & angles for 8 sectors
            for (int i = 0; i < 8; i++)
            {
                if (!_dirActive[i]) continue;
                float angleDeg = i * 45f; // 0=N(front), 90=E, 180=S, 270=W
                float rad = angleDeg * Mathf.Deg2Rad;

                // Direction vector in screen space: 0deg is up (negative Y), so invert sin for Y
                Vector2 dir = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
                Vector2 pos = center + dir * r;

                // Draw rotated triangle pointing outward
                Color prev = GUI.color;
                var c = Color.red;
                c.a = cfgOpacity.Value;
                GUI.color = c;
                Vector2 pivot = pos;
                GUIUtility.RotateAroundPivot(angleDeg, pivot);
                Rect rect = new Rect(pos.x - size * 0.5f, pos.y - size * 0.5f, size, size);
                GUI.DrawTexture(rect, _triTex);
                GUI.matrix = Matrix4x4.identity; // reset rotation
                GUI.color = prev;
            }
        }

        // Create a simple right-pointing triangle texture procedurally
        private static Texture2D MakeTriangleTexture(Color col)
        {
            int w = 64, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color32[w * h];
            Color32 on = new Color(col.r, col.g, col.b, 1f);
            Color32 off = new Color(0, 0, 0, 0);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Right-pointing isosceles triangle filling the square
                    // Keep those pixels where x >= |y - h/2|
                    int dx = x;
                    int dy = Mathf.Abs(y - h / 2);
                    bool inside = dx >= dy;
                    pixels[y * w + x] = inside ? on : off;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }
    }
}