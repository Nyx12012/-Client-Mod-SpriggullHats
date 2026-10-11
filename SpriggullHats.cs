using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Alta.Customization;
using Alta.Intelligence;
using HarmonyLib;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(SpriggullHats.Mod), "SpriggullHats", SpriggullHats.Mod.Version, "Shadow")]
[assembly: MelonGame("Alta", "A Township Tale")]

namespace SpriggullHats
{
    public class Mod : MelonMod
    {
        public const string Version = "1.0.0";
        internal static MelonLogger.Instance Log;
        float nextScan;
        float nextCheck;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            if (Application.isBatchMode) return;
            Settings.Load();
            Log.Msg("SpriggullHats " + Version + " ready.");
        }

        public override void OnUpdate()
        {
            if (Application.isBatchMode) return;
            float now = Time.unscaledTime;
            if (now >= nextCheck)
            {
                nextCheck = now + 2f;
                if (Settings.Changed())
                {
                    Settings.Load();
                    Hats.Clear();
                    nextScan = 0f;
                    Log.Msg("settings.cfg changed, hats redone.");
                }
            }
            if (now < nextScan) return;
            nextScan = now + 2f;
            try { Hats.Scan(); }
            catch (Exception e) { Log.Error("Hat scan failed: " + e); }
        }
    }

    internal static class Hats
    {
        static readonly Dictionary<int, GameObject> Worn = new Dictionary<int, GameObject>();
        static readonly HashSet<int> Bare = new HashSet<int>();
        static readonly Dictionary<string, Vector3[]> Points = new Dictionary<string, Vector3[]>();
        static readonly List<int> Gone = new List<int>();
        static bool reported;
        static bool warnedBone;

        internal static void Scan()
        {
            Gone.Clear();
            foreach (KeyValuePair<int, GameObject> pair in Worn) if (pair.Value == null) Gone.Add(pair.Key);
            foreach (int id in Gone) Worn.Remove(id);
            if (!Settings.Enabled) return;
            SpriggullFlail[] found = UnityEngine.Object.FindObjectsOfType<SpriggullFlail>();
            if (found.Length == 0 || Cosmetic.Busy()) return;
            foreach (SpriggullFlail spriggull in found)
            {
                int id = spriggull.GetInstanceID();
                if (Worn.ContainsKey(id) || Bare.Contains(id)) continue;
                GameObject hat = null;
                try { hat = Dress(spriggull.transform); }
                catch (Exception e) { Mod.Log.Warning("No hat for " + spriggull.name + ": " + e.Message); }
                if (hat != null) Worn[id] = hat;
                else Bare.Add(id);
            }
        }

        internal static void Clear()
        {
            foreach (GameObject hat in Worn.Values) if (hat != null) UnityEngine.Object.Destroy(hat);
            Worn.Clear();
            Bare.Clear();
            Model.Reset();
            Cosmetic.Sync();
            reported = false;
            warnedBone = false;
        }

        static GameObject Dress(Transform root)
        {
            SkinnedMeshRenderer body = Body(root);
            if (body == null) return null;
            Transform[] bones = body.bones;
            Vector3 up = root.up;
            string why;
            int index = HeadIndex(bones, root, up, out why);
            if (index < 0) return null;
            Transform head = bones[index];
            float grown = Mathf.Abs(root.lossyScale.y);
            if (grown < 0.01f) grown = 1f;
            Vector3 forward = Vector3.ProjectOnPlane(root.forward, up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.ProjectOnPlane(root.right, up);
            forward.Normalize();
            string how;
            Vector3 top = Top(body, index, head, up, grown, out how) + forward * (Settings.Forward * grown);
            float scale = Settings.Size * grown;

            Quaternion rotation = Quaternion.AngleAxis(Settings.Tilt, forward) * Quaternion.LookRotation(forward, up);

            GameObject hat = new GameObject("SpriggullHat");
            hat.layer = body.gameObject.layer;
            hat.transform.SetParent(head, false);
            hat.transform.position = top + up * (Settings.Lift * grown - Model.Sink * scale);
            hat.transform.rotation = rotation;
            Vector3 held = head.lossyScale;
            float parent = (Mathf.Abs(held.x) + Mathf.Abs(held.y) + Mathf.Abs(held.z)) / 3f;
            hat.transform.localScale = Vector3.one * (scale / Mathf.Max(parent, 0.0001f));
            if (Cosmetic.Ready) Cosmetic.Wear(hat);
            else
            {
                hat.AddComponent<MeshFilter>().sharedMesh = Model.Mesh;
                hat.AddComponent<MeshRenderer>().sharedMaterials = Model.Materials(body);
            }

            if (!reported)
            {
                reported = true;
                try { Report(root, body, index, why, how, top, head); }
                catch (Exception e) { Mod.Log.Warning("Could not write rig.txt: " + e.Message); }
            }
            return hat;
        }

        static SkinnedMeshRenderer Body(Transform root)
        {
            SkinnedMeshRenderer best = null;
            long most = -1;
            foreach (SkinnedMeshRenderer r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.sharedMesh == null || r.bones == null || r.bones.Length == 0) continue;
                long score = r.sharedMesh.vertexCount;
                if (r.name.IndexOf("LOD0", StringComparison.OrdinalIgnoreCase) >= 0) score += 10000000L;
                if (r.gameObject.activeInHierarchy) score += 100000000L;
                if (score > most) { most = score; best = r; }
            }
            return best;
        }

        static int HeadIndex(Transform[] bones, Transform root, Vector3 up, out string why)
        {
            if (Settings.Bone.Length > 0)
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] != null && string.Equals(bones[i].name, Settings.Bone, StringComparison.OrdinalIgnoreCase))
                    {
                        why = "bone from settings";
                        return i;
                    }
                }
                if (!warnedBone) { warnedBone = true; Mod.Log.Warning("No bone named '" + Settings.Bone + "', finding the head by itself."); }
            }
            int best = Named(bones, true);
            if (best < 0) best = Named(bones, false);
            if (best >= 0) { why = "named like a head"; return best; }
            float high = float.MinValue;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                float h = Vector3.Dot(bones[i].position - root.position, up);
                if (h > high) { high = h; best = i; }
            }
            why = "highest bone, no head bone found";
            return best;
        }

        static int Named(Transform[] bones, bool strict)
        {
            int best = -1;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                string name = bones[i].name.ToLowerInvariant();
                if (!name.Contains("head")) continue;
                if (strict && (name.Contains("end") || name.Contains("nub") || name.Contains("tip") || name.Contains("top"))) continue;
                if (best < 0 || name.Length < bones[best].name.Length) best = i;
            }
            return best;
        }

        static Vector3 Top(SkinnedMeshRenderer body, int index, Transform head, Vector3 up, float grown, out string how)
        {
            Vector3[] local = HeadPoints(body.sharedMesh, index);
            if (local.Length >= 8)
            {
                List<Vector3> world = new List<Vector3>(local.Length);
                foreach (Vector3 p in local) world.Add(head.TransformPoint(p));
                Vector3 eyes;
                if (Eyes(head, out eyes))
                {
                    List<Vector3> skull = new List<Vector3>();
                    float reach = 0.06f * grown;
                    foreach (Vector3 p in world) if (Vector3.ProjectOnPlane(p - eyes, up).magnitude < reach) skull.Add(p);
                    if (skull.Count >= 8)
                    {
                        how = "mesh above the eyes, " + skull.Count + " of " + local.Length + " head vertices";
                        return Peak(skull, up);
                    }
                }
                how = "mesh, " + local.Length + " head vertices";
                return Peak(world, up);
            }
            foreach (Transform child in head)
            {
                string name = child.name.ToLowerInvariant();
                if (name.Contains("end") || name.Contains("top") || name.Contains("tip"))
                {
                    how = "end bone " + child.name;
                    return child.position;
                }
            }
            how = "guess, 0.1 m above the head bone";
            return head.position + up * (0.1f * grown);
        }

        static bool Eyes(Transform head, out Vector3 middle)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (Transform t in head.GetComponentsInChildren<Transform>(true))
            {
                string name = t.name.ToLowerInvariant();
                if (!name.Contains("eye") || name.Contains("lid")) continue;
                sum += t.position;
                count++;
            }
            middle = count > 0 ? sum / count : head.position;
            return count > 0;
        }

        static Vector3 Peak(List<Vector3> points, Vector3 up)
        {
            float hi = float.MinValue;
            float lo = float.MaxValue;
            foreach (Vector3 p in points)
            {
                float h = Vector3.Dot(p, up);
                if (h > hi) hi = h;
                if (h < lo) lo = h;
            }
            float cut = hi - (hi - lo) * 0.12f;
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (Vector3 p in points)
            {
                if (Vector3.Dot(p, up) < cut) continue;
                sum += p;
                count++;
            }
            Vector3 centre = sum / count;
            return centre + up * (hi - Vector3.Dot(centre, up));
        }

        static Vector3[] HeadPoints(Mesh mesh, int bone)
        {
            if (mesh == null) return new Vector3[0];
            string key = mesh.GetInstanceID() + ":" + bone;
            Vector3[] points;
            if (Points.TryGetValue(key, out points)) return points;
            List<Vector3> list = new List<Vector3>();
            if (mesh.isReadable)
            {
                Vector3[] vertices = mesh.vertices;
                BoneWeight[] weights = mesh.boneWeights;
                Matrix4x4[] bind = mesh.bindposes;
                if (weights.Length == vertices.Length && bone < bind.Length)
                    for (int i = 0; i < vertices.Length; i++)
                        if (Strongest(weights[i]) == bone) list.Add(bind[bone].MultiplyPoint3x4(vertices[i]));
            }
            points = list.ToArray();
            Points[key] = points;
            return points;
        }

        static int Strongest(BoneWeight w)
        {
            int bone = w.boneIndex0;
            float most = w.weight0;
            if (w.weight1 > most) { bone = w.boneIndex1; most = w.weight1; }
            if (w.weight2 > most) { bone = w.boneIndex2; most = w.weight2; }
            if (w.weight3 > most) bone = w.boneIndex3;
            return bone;
        }

        static void Report(Transform root, SkinnedMeshRenderer body, int index, string why, string how, Vector3 top, Transform head)
        {
            Mesh mesh = body.sharedMesh;
            Transform[] bones = body.bones;
            int[] owned = new int[bones.Length];
            if (mesh.isReadable)
            {
                foreach (BoneWeight w in mesh.boneWeights)
                {
                    int b = Strongest(w);
                    if (b >= 0 && b < owned.Length) owned[b]++;
                }
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("SpriggullHats " + Mod.Version + " rig report");
            sb.AppendLine("body: " + PathOf(body.transform, root) + ", mesh " + mesh.name + ", " + mesh.vertexCount + " vertices, readable " + mesh.isReadable);
            sb.AppendLine("head: " + bones[index].name + " (" + why + ")");
            sb.AppendLine("top: " + how + ", " + F(Vector3.Dot(top - head.position, root.up)) + " m above the head bone");
            sb.AppendLine("hat: " + Cosmetic.Status);
            sb.AppendLine("spriggull scale " + F(root.lossyScale.y) + ", head bone scale " + F(head.lossyScale.x) + ", plain hat shader " + Model.ShaderName);
            sb.AppendLine();
            sb.AppendLine("bones: index, height above the spriggull's feet in m, vertices it owns, path");
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) { sb.AppendLine(i + "  missing"); continue; }
                sb.AppendLine(i + "  " + F(Vector3.Dot(bones[i].position - root.position, root.up)) + "  " + owned[i] + "  " + PathOf(bones[i], root));
            }
            sb.AppendLine();
            sb.AppendLine("renderers:");
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                sb.AppendLine("  " + r.GetType().Name + "  " + PathOf(r.transform, root) + "  active " + r.gameObject.activeInHierarchy);
            File.WriteAllText(Settings.RigPath, sb.ToString());
            Mod.Log.Msg("First hat on bone " + bones[index].name + " (" + why + "), top from " + how + ". Details in UserData\\SpriggullHats\\rig.txt");
        }

        static string PathOf(Transform t, Transform root)
        {
            string path = t.name;
            while (t != root && t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        static string F(float v)
        {
            return v.ToString("0.000", CultureInfo.InvariantCulture);
        }
    }

    internal static class Cosmetic
    {
        const float Width = 0.13f;
        static readonly FieldInfo PickupField = AccessTools.Field(typeof(PlayerCosmeticDefinition), "pickup");
        static readonly FieldInfo BaseField = AccessTools.Field(typeof(CosmeticPickup), "baseMaterial");
        static readonly Quaternion Upright = Quaternion.Euler(-90f, 0f, 0f);

        static AddressableSession session;
        static Task<PlayerCosmeticDefinition> loading;
        static float started;
        static uint hash;
        static bool failed;
        static Mesh[] meshes;
        static Material[] materials;
        static Vector3 bottom;
        static float scale;

        internal static string Status = "plain hat";
        internal static bool Ready { get { return meshes != null; } }

        internal static bool Busy()
        {
            if (Settings.Cosmetic == 0 || failed || Ready) return false;
            if (loading == null)
            {
                hash = Settings.Cosmetic;
                session = new AddressableSession();
                started = Time.unscaledTime;
                try { loading = Purchasable.Get<PlayerCosmeticDefinition>(session, hash); }
                catch (Exception e) { Fail("loading it threw " + e.Message); return false; }
                return true;
            }
            if (!loading.IsCompleted)
            {
                if (Time.unscaledTime - started < 30f) return true;
                Fail("it took over 30 seconds to load");
                return false;
            }
            if (loading.IsFaulted || loading.IsCanceled) { Fail("loading it failed: " + loading.Exception); return false; }
            if (loading.Result == null) { Fail("the game has no cosmetic with that hash"); return false; }
            try { Build(loading.Result); }
            catch (Exception e) { Fail("building it threw " + e); }
            return false;
        }

        internal static void Sync()
        {
            if (Settings.Cosmetic == hash && (Ready || failed || loading != null)) return;
            if (materials != null) foreach (Material m in materials) if (m != null) UnityEngine.Object.Destroy(m);
            if (session != null) { try { session.Dispose(); } catch { } }
            session = null;
            loading = null;
            failed = false;
            meshes = null;
            materials = null;
            hash = 0;
            Status = "plain hat";
        }

        internal static void Wear(GameObject hat)
        {
            for (int i = 0; i < meshes.Length; i++)
            {
                GameObject part = new GameObject("SpriggullHat " + meshes[i].name);
                part.layer = hat.layer;
                part.transform.SetParent(hat.transform, false);
                part.transform.localRotation = Upright;
                part.transform.localScale = Vector3.one * scale;
                part.transform.localPosition = Upright * (-bottom * scale);
                part.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                part.AddComponent<MeshRenderer>().sharedMaterial = materials[i];
            }
        }

        static void Build(PlayerCosmeticDefinition definition)
        {
            CosmeticPickup pickup = PickupField != null ? PickupField.GetValue(definition) as CosmeticPickup : null;
            Material baseMaterial = pickup != null && BaseField != null ? BaseField.GetValue(pickup) as Material : null;
            if (baseMaterial == null) { Fail("its pickup has no base material"); return; }
            CustomizationArguments[] arguments = definition.CreateDefaultArguments();
            List<Mesh> found = new List<Mesh>();
            List<Material> painted = new List<Material>();
            Bounds bounds = new Bounds();
            foreach (ICustomizationPart part in definition.GetVisual().Parts)
            {
                ICosmeticMeshPartBase meshPart = part as ICosmeticMeshPartBase;
                if (meshPart == null || meshPart.Lods == null || meshPart.Lods.Length == 0 || meshPart.Lods[0] == null) continue;
                Mesh mesh = meshPart.Lods[0];
                mesh.RecalculateBounds();
                if (found.Count == 0) bounds = mesh.bounds;
                else bounds.Encapsulate(mesh.bounds);
                int index = meshPart.CopyColorFrom != null ? meshPart.CopyColorFrom.PartIndex : meshPart.PartIndex;
                found.Add(mesh);
                painted.Add(Paint(baseMaterial, meshPart, arguments, index));
            }
            if (found.Count == 0) { Fail("it has no mesh"); return; }
            bottom = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z);
            scale = Width / Mathf.Max(Mathf.Max(bounds.size.x, bounds.size.y), 0.001f);
            meshes = found.ToArray();
            materials = painted.ToArray();
            Status = "cosmetic " + definition.DisplayName + " (" + hash + "), " + meshes.Length + " part(s), shader " + baseMaterial.shader.name;
            Mod.Log.Msg("Hat is the " + definition.DisplayName + ".");
        }

        static Material Paint(Material baseMaterial, ICosmeticMeshPartBase meshPart, CustomizationArguments[] arguments, int index)
        {
            Material m = new Material(baseMaterial);
            TwoColors colors = arguments != null && index >= 0 && index < arguments.Length ? arguments[index] as TwoColors : null;
            if (meshPart.CopyColorFrom != null) meshPart = meshPart.CopyColorFrom;
            if (colors != null)
            {
                if (colors.Primary != null) colors.Primary.ApplyA(m);
                if (colors.Secondary != null) colors.Secondary.ApplyB(m);
            }
            else
            {
                CosmeticColor primary;
                CosmeticColor secondary;
                meshPart.GetCustomPickupColors(out primary, out secondary);
                if (primary != null) primary.ApplyA(m);
                if (secondary != null) secondary.ApplyB(m);
            }
            m.SetTexture("_MainTex", meshPart.Texture);
            m.SetTexture("_BumpMap", meshPart.Normal);
            if (meshPart.IsAlphaBlending)
            {
                m.SetFloat("_SrcBlend", 1f);
                m.SetFloat("_DstBlend", 10f);
                m.SetFloat("_ZWrite", 0f);
                m.renderQueue = 3000;
            }
            return m;
        }

        static void Fail(string why)
        {
            failed = true;
            Status = "plain hat, cosmetic " + Settings.Cosmetic + " failed: " + why;
            Mod.Log.Warning("Cosmetic hat " + Settings.Cosmetic + " failed, using the plain hat: " + why);
        }
    }

    internal static class Model
    {
        internal const float Sink = 0.008f;
        internal static string ShaderName = "none";
        static Mesh mesh;
        static Material felt;
        static Material band;

        internal static Mesh Mesh
        {
            get
            {
                if (mesh == null) mesh = Build();
                return mesh;
            }
        }

        internal static Material[] Materials(Renderer donor)
        {
            if (felt == null) felt = Paint(donor, Settings.Hat, "SpriggullHat felt");
            if (band == null) band = Paint(donor, Settings.Band, "SpriggullHat band");
            return new Material[] { felt, band };
        }

        internal static void Reset()
        {
            if (felt != null) UnityEngine.Object.Destroy(felt);
            if (band != null) UnityEngine.Object.Destroy(band);
            felt = null;
            band = null;
        }

        static Material Paint(Renderer donor, Color color, string name)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null && donor != null && donor.sharedMaterial != null) shader = donor.sharedMaterial.shader;
            if (shader == null) shader = Shader.Find("Sprites/Default");
            Material m = new Material(shader);
            m.name = name;
            ShaderName = shader.name;
            if (m.HasProperty("_Color"))
            {
                m.SetColor("_Color", color);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", Texture2D.whiteTexture);
            }
            else if (m.HasProperty("_MainTex"))
            {
                Texture2D solid = new Texture2D(2, 2);
                solid.SetPixels(new Color[] { color, color, color, color });
                solid.Apply();
                m.SetTexture("_MainTex", solid);
            }
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.2f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            return m;
        }

        static Mesh Build()
        {
            const int Sides = 32;
            const float Brim = 0.062f;
            const float Thick = 0.005f;
            const float Base = 0.036f;
            const float Crown = 0.04f;
            const float Tall = 0.08f;
            const float Ribbon = 0.0372f;
            const float RibbonTall = 0.016f;
            float ribbonTop = Thick + RibbonTall;
            float above = Base + (Crown - Base) * (RibbonTall / (Tall - Thick));
            Vector2[] path =
            {
                new Vector2(0f, 0f), new Vector2(Brim, 0f), new Vector2(Brim, Thick), new Vector2(Ribbon, Thick),
                new Vector2(Ribbon, ribbonTop), new Vector2(above, ribbonTop), new Vector2(Crown, Tall), new Vector2(0f, Tall)
            };
            bool[] isBand = { false, false, false, true, true, false, false };

            List<Vector3> vertices = new List<Vector3>();
            List<int> feltTris = new List<int>();
            List<int> bandTris = new List<int>();
            for (int s = 0; s < path.Length - 1; s++)
            {
                Vector2 a = path[s];
                Vector2 b = path[s + 1];
                Vector2 outward = new Vector2(b.y - a.y, a.x - b.x);
                int start = vertices.Count;
                for (int i = 0; i < Sides; i++)
                {
                    float angle = i * Mathf.PI * 2f / Sides;
                    float cos = Mathf.Cos(angle);
                    float sin = Mathf.Sin(angle);
                    vertices.Add(new Vector3(a.x * cos, a.y, a.x * sin));
                    vertices.Add(new Vector3(b.x * cos, b.y, b.x * sin));
                }
                List<int> tris = isBand[s] ? bandTris : feltTris;
                for (int i = 0; i < Sides; i++)
                {
                    int a0 = start + i * 2;
                    int b0 = a0 + 1;
                    int a1 = start + ((i + 1) % Sides) * 2;
                    int b1 = a1 + 1;
                    float mid = (i + 0.5f) * Mathf.PI * 2f / Sides;
                    Vector3 want = new Vector3(outward.x * Mathf.Cos(mid), outward.y, outward.x * Mathf.Sin(mid));
                    Face(tris, vertices, a0, b0, a1, want);
                    Face(tris, vertices, a1, b0, b1, want);
                }
            }

            Mesh m = new Mesh();
            m.name = "SpriggullHat";
            m.SetVertices(vertices);
            Color[] colors = new Color[vertices.Count];
            Vector2[] uv = new Vector2[vertices.Count];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            m.colors = colors;
            m.uv = uv;
            m.subMeshCount = 2;
            m.SetTriangles(feltTris, 0);
            m.SetTriangles(bandTris, 1);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static void Face(List<int> tris, List<Vector3> v, int i0, int i1, int i2, Vector3 want)
        {
            Vector3 normal = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]);
            if (normal.sqrMagnitude < 1e-14f) return;
            tris.Add(i0);
            if (Vector3.Dot(normal, want) >= 0f) { tris.Add(i1); tris.Add(i2); }
            else { tris.Add(i2); tris.Add(i1); }
        }
    }

    internal static class Settings
    {
        internal static bool Enabled;
        internal static float Size;
        internal static float Lift;
        internal static float Forward;
        internal static float Tilt;
        internal static Color Hat;
        internal static Color Band;
        internal static string Bone;
        internal static uint Cosmetic;
        static DateTime stamp;

        static readonly string[] Template =
        {
            "# SpriggullHats settings. Save this file and the hats update in game within two seconds.",
            "# enabled: 1 for hats, 0 for none",
            "enabled = 1",
            "# size: 1 is tiny, 2 is twice as big",
            "size = 1",
            "# lift: metres to raise the hat, negative to sink it into the head",
            "lift = 0",
            "# forward: metres to move the hat towards the beak, negative to move it back",
            "forward = 0",
            "# tilt: degrees the hat leans to one side",
            "tilt = 8",
            "# cosmetic: the player hat to use. 64982 Villager Top Hat, 26131 Battered Top Hat, 0 for the plain hat",
            "cosmetic = 64982",
            "# hat and band colours of the plain hat, as hex RRGGBB",
            "hat = 1C1A1F",
            "band = 8C1D2B",
            "# bone: leave empty to find the head by itself, or put a bone name from rig.txt",
            "bone ="
        };

        static string Folder { get { return Path.Combine(MelonEnvironment.UserDataDirectory, "SpriggullHats"); } }
        static string FilePath { get { return Path.Combine(Folder, "settings.cfg"); } }
        internal static string RigPath { get { return Path.Combine(Folder, "rig.txt"); } }

        internal static bool Changed()
        {
            try { return File.Exists(FilePath) && File.GetLastWriteTimeUtc(FilePath) != stamp; }
            catch { return false; }
        }

        internal static void Load()
        {
            Enabled = true;
            Size = 1f;
            Lift = 0f;
            Forward = 0f;
            Tilt = 8f;
            Hat = new Color32(0x1C, 0x1A, 0x1F, 0xFF);
            Band = new Color32(0x8C, 0x1D, 0x2B, 0xFF);
            Bone = "";
            Cosmetic = 64982;
            try
            {
                Directory.CreateDirectory(Folder);
                if (!File.Exists(FilePath)) File.WriteAllLines(FilePath, Template);
                stamp = File.GetLastWriteTimeUtc(FilePath);
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string value = line.Substring(eq + 1).Trim();
                    float number;
                    bool isNumber = float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
                    Color color;
                    if (key == "enabled") Enabled = value != "0" && value.ToLowerInvariant() != "false" && value.ToLowerInvariant() != "off";
                    else if (key == "size" && isNumber && number > 0f) Size = number;
                    else if (key == "lift" && isNumber) Lift = number;
                    else if (key == "forward" && isNumber) Forward = number;
                    else if (key == "tilt" && isNumber) Tilt = number;
                    else if (key == "hat" && ColorUtility.TryParseHtmlString("#" + value.TrimStart('#'), out color)) Hat = color;
                    else if (key == "band" && ColorUtility.TryParseHtmlString("#" + value.TrimStart('#'), out color)) Band = color;
                    else if (key == "bone") Bone = value;
                    else if (key == "cosmetic")
                    {
                        uint id;
                        if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) Cosmetic = id;
                    }
                }
            }
            catch (Exception e) { Mod.Log.Warning("Reading settings.cfg failed, using defaults: " + e.Message); }
        }
    }
}
