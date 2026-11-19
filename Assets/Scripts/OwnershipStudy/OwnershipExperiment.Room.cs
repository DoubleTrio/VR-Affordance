using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace OwnershipStudy
{
    public sealed partial class OwnershipExperiment
    {
        private const float TableY = 0.74f;
        private const float PanelZ = 0.62f;
        private const float PanelY = 1.28f;

        [SerializeField] private Shader litShader;
        [SerializeField] private Shader unlitShader;

        private Transform rig;
        private Transform head;
        private readonly Transform[] hands = new Transform[2];
        private readonly bool[] handTracked = new bool[2];
        private bool headTracked;
        private Transform startZone;
        private Transform placementPad;
        private Transform cueMarker;
        private GameObject agent;
        private GameObject panel;
        private readonly Dictionary<Collider, Action> buttons = new Dictionary<Collider, Action>();
        private readonly List<Material> materials = new List<Material>();
        private readonly Texture2D[] previews = new Texture2D[3];
        private readonly LineRenderer[] pointers = new LineRenderer[2];
        private Material surface;
        private Material panelMaterial;
        private Material buttonMaterial;
        private Material accent;
        private PlayableGraph agentGraph;
        private Animator agentAnimator;
        private Transform agentHead;
        private readonly System.Random gazeRandom = new System.Random();
        private Vector3 gazePoint;
        private double nextGaze;

        private void BuildRoom()
        {
            surface = Paint(new Color(0.40f, 0.29f, 0.22f));
            panelMaterial = Paint(new Color(0.055f, 0.08f, 0.12f), true);
            buttonMaterial = Paint(new Color(0.11f, 0.27f, 0.36f), true);
            accent = Paint(new Color(0.36f, 0.88f, 0.86f), true);
            Material floor = Paint(new Color(0.31f, 0.35f, 0.39f));
            Material walls = Paint(new Color(0.58f, 0.65f, 0.68f));
            Cube("Floor", new Vector3(0, -0.025f, 1), new Vector3(9, 0.05f, 9), floor);
            Cube("Far wall", new Vector3(0, 1.65f, 3.05f), new Vector3(8, 3.3f, 0.08f), walls);
            Cube("Left wall", new Vector3(-3.5f, 1.65f, 0.7f), new Vector3(0.08f, 3.3f, 5), walls);
            Cube("Right wall", new Vector3(3.5f, 1.65f, 0.7f), new Vector3(0.08f, 3.3f, 5), walls);
            Cube("Tabletop", new Vector3(0, TableY - 0.025f, 0.58f), new Vector3(1.2f, 0.05f, 0.62f), surface);
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                Cube("Table leg", new Vector3(x * 0.53f, 0.35f, 0.58f + z * 0.25f), new Vector3(0.055f, 0.7f, 0.055f), surface);

            placementPad = new GameObject("Placement center").transform;
            placementPad.position = new Vector3(0, TableY + 0.007f, 0.39f);
            Disc("Placement pad", placementPad.position, 0.225f, 0.005f, buttonMaterial);
            Text("PLACE", new Vector3(0, TableY + 0.012f, 0.33f), 0.019f, Color.white, Quaternion.Euler(90, 0, 0));

            cueMarker = Disc("Target highlight", Vector3.zero, 0.157f, 0.004f,
                Paint(new Color(1f, 0.76f, 0.18f), true)).transform;
            Text("TARGET", Vector3.up * 0.155f, 0.018f, new Color(1f, 0.76f, 0.18f))
                .transform.SetParent(cueMarker, true);
            cueMarker.gameObject.SetActive(false);
            startZone = new GameObject("Start zone").transform;
            startZone.position = new Vector3(0.16f, TableY + 0.085f, 0.30f);
            Disc("Start zone marker", new Vector3(0.16f, TableY + 0.006f, 0.30f), 0.11f, 0.006f, accent)
                .transform.SetParent(startZone, true);
            var startRing = new GameObject("Controller height marker").AddComponent<LineRenderer>();
            startRing.transform.SetParent(startZone, false);
            startRing.sharedMaterial = accent;
            startRing.useWorldSpace = false;
            startRing.loop = true;
            startRing.widthMultiplier = 0.0016f;
            startRing.positionCount = 32;
            for (int i = 0; i < startRing.positionCount; i++)
            {
                float angle = i * 2 * Mathf.PI / startRing.positionCount;
                startRing.SetPosition(i, startZoneRadiusMeters * new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)));
            }
            Text("REACH START", new Vector3(0.16f, TableY + 0.01f, 0.30f), 0.013f, Color.black,
                Quaternion.Euler(90, 0, 0)).transform.SetParent(startZone, true);

            var light = new GameObject("Room light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.transform.rotation = Quaternion.Euler(53, -27, 0);
            var fill = new GameObject("Fill light").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.45f;
            fill.transform.rotation = Quaternion.Euler(20, 149, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.65f, 0.65f, 0.68f);

            rig = new GameObject("Seated tracking origin").transform;
            var cameraObject = new GameObject("Participant view");
            cameraObject.tag = "MainCamera";
            head = cameraObject.transform;
            head.SetParent(rig, false);
            head.localPosition = new Vector3(0, 1.2f, 0);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.035f;
            camera.farClipPlane = 30f;
            cameraObject.AddComponent<AudioListener>();

            for (int i = 0; i < hands.Length; i++)
            {
                hands[i] = new GameObject(i == 0 ? "Left controller" : "Right controller").transform;
                hands[i].SetParent(rig, false);
                Disc("Controller indicator", Vector3.zero, 0.04f, 0.018f, accent).transform.SetParent(hands[i], false);
                pointers[i] = hands[i].gameObject.AddComponent<LineRenderer>();
                pointers[i].sharedMaterial = accent;
                pointers[i].startWidth = 0.004f;
                pointers[i].endWidth = 0.0015f;
                pointers[i].positionCount = 2;
                pointers[i].enabled = false;
            }

            for (int i = 0; i < mugs.Length; i++)
                ImportMug(i);
            SetMugsVisible(false);
            BuildAgent();
        }

        private void ImportMug(int index)
        {
            var slot = new GameObject(PositionNames[index] + " mug position");
            slot.transform.position = new Vector3((index - 1) * 0.19f, TableY + 0.001f, 0.58f);
            mugSlots[index] = slot.transform;

            var mug = new GameObject(MugNames[index] + " mug");
            mugs[index] = mug;
            GameObject model = Resources.Load<GameObject>(ResourceRoot + "Mugs/" + MugFiles[index]);
            previews[index] = Resources.Load<Texture2D>(ResourceRoot + "Previews/" + MugFiles[index]);
            if (model == null)
                throw new InvalidOperationException($"Missing imported mug model: {MugFiles[index]}.");
            GameObject visual = Instantiate(model, mug.transform);
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                throw new InvalidOperationException($"No mesh renderers in {MugFiles[index]}.");
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float scale = 0.11f / bounds.size.y;
            visual.transform.localScale *= scale;
            visual.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
            var box = mug.AddComponent<BoxCollider>();
            box.center = Vector3.up * 0.055f;
            box.size = new Vector3(0.115f, 0.11f, 0.11f);
            box.isTrigger = true;
            grabVolumes[index] = box;
        }

        private void ResetMugs()
        {
            for (int position = 0; position < mugSlots.Length; position++)
                mugs[Trial.MugsAtPositions[position]].transform.SetPositionAndRotation(mugSlots[position].position, Quaternion.identity);
            SetMugsVisible(true);
            float x = taskHand == 0 ? -0.16f : 0.16f;
            startZone.position = new Vector3(x, startZone.position.y, startZone.position.z);
        }

        private void SetMugsVisible(bool visible)
        {
            foreach (GameObject mug in mugs) mug.SetActive(visible);
        }

        private void UpdatePoses()
        {
            headTracked = ReadPose(InputDevices.GetDeviceAtXRNode(XRNode.Head), head);
            for (int i = 0; i < hands.Length; i++)
            {
                handTracked[i] = ReadPose(Controller(i), hands[i]);
                hands[i].gameObject.SetActive(handTracked[i]);
                bool showPointer = handTracked[i] && panel != null && (stage != Stage.Ready || i != taskHand);
                pointers[i].enabled = showPointer;
                if (!showPointer) continue;
                Ray ray = PointerRay(i);
                pointers[i].SetPosition(0, ray.origin);
                pointers[i].SetPosition(1, ray.GetPoint(FindButton(ray, out float distance) == null ? 1.6f : distance));
            }
        }

        private static UnityEngine.XR.InputDevice Controller(int index)
            => InputDevices.GetDeviceAtXRNode(index == 0 ? XRNode.LeftHand : XRNode.RightHand);

        private static bool ReadPose(UnityEngine.XR.InputDevice device, Transform destination)
        {
            if (!device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked ||
                !device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position) ||
                !device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
                return false;
            destination.SetLocalPositionAndRotation(position, rotation);
            return true;
        }

        private void AlignRoomToParticipant()
        {
            float yaw = head.localEulerAngles.y;
            rig.rotation = Quaternion.Euler(0, -yaw, 0);
            rig.position = Vector3.up * 1.20f - rig.rotation * head.localPosition;
        }

        private void ReadControls(out bool leftClick, out bool rightClick, out bool gripPressed, out bool gripReleased)
        {
            leftClick = rightClick = gripPressed = gripReleased = false;
            for (int i = 0; i < hands.Length; i++)
            {
                UnityEngine.XR.InputDevice controller = Controller(i);
                controller.TryGetFeatureValue(CommonUsages.triggerButton, out bool trigger);
                controller.TryGetFeatureValue(CommonUsages.gripButton, out bool grip);
                if (i == 0) leftClick = trigger && !lastTrigger[i];
                else rightClick = trigger && !lastTrigger[i];
                if (i == taskHand)
                {
                    gripPressed = grip && !lastGrip[i];
                    gripReleased = !grip && lastGrip[i];
                }
                lastTrigger[i] = trigger;
                lastGrip[i] = grip;
            }
        }

        private void Click(int index)
        {
            if (!handTracked[index] || (stage == Stage.Ready && index == taskHand))
                return;
            FindButton(PointerRay(index), out _)?.Invoke();
        }

        private Ray PointerRay(int index)
        {
            XRController controller = index == 0 ? XRController.leftHand : XRController.rightHand;
            var position = controller?.TryGetChildControl<Vector3Control>("pointerPosition");
            var rotation = controller?.TryGetChildControl<QuaternionControl>("pointerRotation");
            if (position != null && rotation != null)
                return new Ray(rig.TransformPoint(position.ReadValue()),
                    rig.rotation * rotation.ReadValue() * Vector3.forward);
            return new Ray(hands[index].position, hands[index].forward);
        }

        private Action FindButton(Ray ray, out float closest)
        {
            closest = 2.5f;
            Action action = null;
            foreach (var candidate in buttons)
            {
                if (!candidate.Key.Raycast(ray, out RaycastHit hit, closest)) continue;
                closest = hit.distance;
                action = candidate.Value;
            }
            return action;
        }

        private void OpenPanel(string title, string description)
        {
            ClosePanel();
            panel = Cube("Study panel", new Vector3(0, PanelY, PanelZ), new Vector3(0.95f, 0.36f, 0.015f), panelMaterial);
            Text(title, new Vector3(0, PanelY + 0.135f, PanelZ - 0.01f), 0.031f, Color.white).transform.SetParent(panel.transform, true);
            Text(description, new Vector3(0, PanelY + 0.09f, PanelZ - 0.01f), 0.018f, Color.white).transform.SetParent(panel.transform, true);
        }

        private void ClosePanel()
        {
            buttons.Clear();
            if (panel != null)
            {
                panel.SetActive(false);
                Destroy(panel);
            }
            panel = null;
        }

        private void Button(int index, string label, Action action, Texture2D preview = null)
        {
            float x = (index - 1) * 0.295f;
            float y = PanelY - (preview == null ? 0.126f : 0.018f);
            var button = Cube(label, new Vector3(x, y, PanelZ - 0.015f),
                new Vector3(0.265f, preview == null ? 0.063f : 0.18f, 0.012f), buttonMaterial);
            button.transform.SetParent(panel.transform, true);
            BoxCollider collider = button.AddComponent<BoxCollider>();
            buttons.Add(collider, action);
            Physics.SyncTransforms();
            Text(label, new Vector3(x, y - (preview == null ? 0 : 0.062f), PanelZ - 0.024f),
                0.023f, Color.white).transform.SetParent(panel.transform, true);
            if (preview != null)
                Picture(preview, new Vector3(x, y + 0.016f, PanelZ - 0.028f), 0.13f);
        }

        private void OwnershipCard(int index, string label, Texture2D preview)
        {
            float x = (index - 1) * 0.285f;
            Text(label, new Vector3(x, PanelY - 0.056f, PanelZ - 0.024f),
                0.019f, Color.white).transform.SetParent(panel.transform, true);
            if (preview != null)
                Picture(preview, new Vector3(x, PanelY + 0.012f, PanelZ - 0.027f), 0.115f);
        }

        private void Picture(Texture2D texture, Vector3 position, float size)
        {
            var picture = Primitive(PrimitiveType.Quad, texture.name, position, new Vector3(size, size, 1), PreviewMaterial(texture));
            picture.transform.SetParent(panel.transform, true);
        }

        private readonly Dictionary<Texture2D, Material> previewMaterials = new Dictionary<Texture2D, Material>();

        private Material PreviewMaterial(Texture2D texture)
        {
            if (previewMaterials.TryGetValue(texture, out Material existing)) return existing;
            Material material = Paint(Color.white, true);
            material.SetTexture("_BaseMap", texture);
            MakeCutout(material);
            previewMaterials.Add(texture, material);
            return material;
        }

        private static void MakeCutout(Material material)
        {
            material.SetFloat("_AlphaClip", 1);
            material.SetFloat("_Cutoff", 0.4f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.AlphaTest;
        }

        private TextMesh Text(string text, Vector3 position, float size, Color color, Quaternion? rotation = null)
        {
            var label = new GameObject(text).AddComponent<TextMesh>();
            label.text = text;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 64;
            label.characterSize = size * 10 / 64;
            label.color = color;
            label.transform.SetPositionAndRotation(position, rotation ?? Quaternion.identity);
            return label;
        }

        private Material Paint(Color color, bool unlit = false)
        {
            var material = new Material(unlit ? unlitShader : litShader);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.2f);
            materials.Add(material);
            return material;
        }

        private GameObject Cube(string label, Vector3 position, Vector3 size, Material material)
            => Primitive(PrimitiveType.Cube, label, position, size, material);

        private GameObject Disc(string label, Vector3 position, float diameter, float thickness, Material material)
            => Primitive(PrimitiveType.Cylinder, label, position, new Vector3(diameter, thickness / 2, diameter), material);

        private GameObject Primitive(PrimitiveType type, string label, Vector3 position, Vector3 size, Material material)
        {
            GameObject item = GameObject.CreatePrimitive(type);
            item.name = label;
            item.transform.SetParent(transform, false);
            item.transform.SetPositionAndRotation(position, Quaternion.identity);
            item.transform.localScale = size;
            item.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = item.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }
            return item;
        }

        private void BuildAgent()
        {
            agent = new GameObject("Seated virtual human");
            GameObject model = Resources.Load<GameObject>(ResourceRoot + "Avatar/Young_kev");
            if (model == null) throw new InvalidOperationException("The virtual human model is missing.");
            GameObject visual = Instantiate(model, agent.transform);
            visual.transform.localRotation = Quaternion.Euler(0, 180, 0);
            agentAnimator = visual.GetComponentInChildren<Animator>();
            AnimationClip idle = Resources.LoadAll<AnimationClip>(ResourceRoot + "Avatar/SittingIdle")
                .FirstOrDefault(clip => !clip.name.StartsWith("__preview", StringComparison.Ordinal));
            if (agentAnimator == null || !agentAnimator.isHuman || idle == null)
                throw new InvalidOperationException("The avatar requires a humanoid rig and SittingIdle animation.");

            agentAnimator.applyRootMotion = false;
            agentAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            agentHead = agentAnimator.GetBoneTransform(HumanBodyBones.Head);
            agent.transform.position = new Vector3(0, 0.08f, 1.06f);
            CopyAvatarMaterials(visual);
            agentGraph = PlayableGraph.Create("Study avatar seated idle");
            var output = AnimationPlayableOutput.Create(agentGraph, "Seated animation", agentAnimator);
            var animation = AnimationClipPlayable.Create(agentGraph, idle);
            animation.SetApplyFootIK(false);
            output.SetSourcePlayable(animation);
            agentGraph.Play();

            Cube("Chair seat", new Vector3(0, 0.46f, 1.11f), new Vector3(0.43f, 0.045f, 0.44f), panelMaterial)
                .transform.SetParent(agent.transform, true);
            Cube("Chair back", new Vector3(0, 0.69f, 1.31f), new Vector3(0.42f, 0.48f, 0.045f), panelMaterial)
                .transform.SetParent(agent.transform, true);
            agent.SetActive(false);
        }

        private void CopyAvatarMaterials(GameObject visual)
        {
            var textures = Resources.LoadAll<Texture2D>(ResourceRoot + "Avatar/Textures").ToDictionary(texture => texture.name);
            var converted = new Dictionary<string, Material>();
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>())
            {
                Material[] slots = renderer.sharedMaterials;
                for (int index = 0; index < slots.Length; index++)
                {
                    if (slots[index] == null) continue;
                    string name = slots[index].name;
                    if (!converted.TryGetValue(name, out Material material))
                    {
                        material = Paint(Color.white);
                        material.name = name;
                        if (!textures.TryGetValue(name + "_Diffuse", out Texture2D texture))
                            texture = slots[index].mainTexture as Texture2D;
                        if (name.Contains("Cornea") || name.Contains("Tearline") || name.Contains("Occlusion"))
                        {
                            texture = Texture2D.blackTexture;
                            material.SetColor("_BaseColor", new Color(0, 0, 0, 0));
                            MakeCutout(material);
                        }
                        else if (name.Contains("Transparency"))
                        {
                            MakeCutout(material);
                            material.SetFloat("_Cull", 0);
                        }
                        if (texture != null) material.SetTexture("_BaseMap", texture);
                        converted.Add(name, material);
                    }
                    slots[index] = material;
                }
                renderer.sharedMaterials = slots;
            }
        }

        private void LateUpdate()
        {
            if (agent == null || !agent.activeInHierarchy || agentHead == null) return;
            if (Now >= nextGaze)
            {
                gazePoint = gazeRandom.NextDouble() < 0.63 ? head.position : mugSlots[gazeRandom.Next(3)].position + Vector3.up * 0.06f;
                nextGaze = Now + 1.8 + gazeRandom.NextDouble() * 2.5;
            }
            Vector3 forward = agent.transform.TransformDirection(Vector3.back);
            Vector3 desired = (gazePoint - agentHead.position).normalized;
            desired = Vector3.RotateTowards(forward, desired, 0.46f, 0);
            currentGaze = Vector3.Slerp(currentGaze.sqrMagnitude < 0.1f ? forward : currentGaze, desired,
                1 - Mathf.Exp(-5 * Time.unscaledDeltaTime));
            agentHead.rotation = Quaternion.FromToRotation(forward, currentGaze) * agentHead.rotation;
        }

        private Vector3 currentGaze;

        private void OnDestroy()
        {
            if (agentGraph.IsValid()) agentGraph.Destroy();
            foreach (Material material in materials) Destroy(material);
        }
    }
}
