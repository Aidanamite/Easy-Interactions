using HarmonyLib;
using HMLLibrary;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.UI;
using System.Globalization;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using FMODUnity;
using System.Runtime.Serialization;
using UnityEngine.SceneManagement;

namespace EasyInteractions
{
    public class Main : Mod
    {
        public static float reach = 1;
        Harmony harmony;
        public void Start()
        {
            foreach (var l in Resources.FindObjectsOfTypeAll<Landmark>())
                if (Traverse.Create(l).Field("initialized").GetValue<bool>())
                    Patch_IslandSpawn.Prefix(l, false);
            foreach (var r in FindObjectsOfType<Reciever>())
                Patch_PlaceReciever.Postfix(r);
            foreach (var w in FindObjectsOfType<SteeringWheel>())
                Patch_PlaceSteeringWheel.Postfix(w);
            harmony = new Harmony("com.aidanamite.EasyCraneControls");
            harmony.PatchAll();
            if (SceneManager.GetActiveScene().name == Raft_Network.GameSceneName)
                BlockCreator.RemoveBlockCallStack += BeforeDestroy;
            Log("Mod has been loaded!");
        }

        public void OnModUnload()
        {
            BlockCreator.RemoveBlockCallStack -= BeforeDestroy;
            foreach (var k in Resources.FindObjectsOfTypeAll<KeyboardButtons>())
            {
                k.SetState(false);
                Destroy(k.gameObject);
            }
            harmony.UnpatchAll(harmony.Id);
            Log("Mod has been unloaded!");
        }
        public override void WorldEvent_WorldLoaded() => BlockCreator.RemoveBlockCallStack += BeforeDestroy;

        static void BeforeDestroy(List<Block> blocks, Network_Player player)
        {
            KeyboardButtons[] objs;
            foreach (var block in blocks)
                if (block)
                {
                    objs = block.GetComponentsInChildren<KeyboardButtons>();
                    if (objs != null)
                        foreach (var o in objs)
                            o.SetState(false);
                }
        }

        void ExtraSettingsAPI_Load() => ExtraSettingsAPI_SettingsClose();
        void ExtraSettingsAPI_SettingsClose()
        {
            reach = ExtraSettingsAPI_GetInputValue("reach").ParseFloat();
            reach *= reach;
        }

        string ExtraSettingsAPI_GetInputValue(string SettingName) => "".ToString();
    }

    public static class ExtentionMethods
    {
        static FieldInfo _ownAnchorsAreDown = typeof(EngineControls).GetField("ownAnchorsAreDown", ~BindingFlags.Default);
        public static bool ownAnchorsAreDown(this EngineControls controls) => (bool)_ownAnchorsAreDown.GetValue(controls);
        public static void ownAnchorsAreDown(this EngineControls controls, bool value) => _ownAnchorsAreDown.SetValue(controls,value);
        static MethodInfo _ToggleRemoteAnchorsNetworked = typeof(EngineControls).GetMethod("ToggleRemoteAnchorsNetworked", ~BindingFlags.Default);
        public static void ToggleRemoteAnchorsNetworked(this EngineControls controls) => _ToggleRemoteAnchorsNetworked.Invoke(controls, new object[0]);

        public static bool IsNearbyEngineControl(this Vector3 position, bool? setAnchorState = null)
        {
            var use = Player.UseDistance * Player.UseDistance * Main.reach;
            var flag = false;
            foreach (var control in EngineControls.AllEngineControls)
                if ((position - control.transform.position).sqrMagnitude <= use)
                {
                    if (setAnchorState == null)
                        return true;
                    flag = true;
                    if (control.ownAnchorsAreDown() != setAnchorState.Value)
                        control.ToggleRemoteAnchorsNetworked();
                }
            if (flag)
                EngineControls.AnchorsAreDown = setAnchorState.Value;
            return flag;
        }

        public static void PointEngine(this MotorWheel motor, Vector3 forward)
        {
            forward = forward.normalized;
            var d = (motor.transform.forward + forward).magnitude;
            if (d < 0.2f)
            {
                if (motor.rotatesForward)
                    motor.OnButtonPressed_ChangeDirection();
                if (!motor.engineSwitchOn)
                    motor.OnButtonPressed_ToggleEngine();
            }
            else if (d > 1.8f)
            {
                if (!motor.rotatesForward)
                    motor.OnButtonPressed_ChangeDirection();
                if (!motor.engineSwitchOn)
                    motor.OnButtonPressed_ToggleEngine();
            }
            else
            {
                if (motor.engineSwitchOn)
                    motor.OnButtonPressed_ToggleEngine();
            }

        }

        public static void TrySetMovingForward(this Transform transform, bool reverse = false)
        {
            var position = transform.position;
            var flag = position.IsNearbyEngineControl(false);
            var f = reverse ? -transform.forward : transform.forward;
            var use = Player.UseDistance * Player.UseDistance * Main.reach;
            foreach (var motor in Object.FindObjectsOfType<MotorWheel>())
                if (flag || (position - motor.transform.position).sqrMagnitude <= use)
                    motor.PointEngine(f);
            foreach (var sail in Object.FindObjectsOfType<Sail>())
                if ((position - sail.transform.position).sqrMagnitude <= use)
                {
                    if (!sail.open)
                        sail.Open();
                    sail.SetRotation(Quaternion.LookRotation(sail.transform.InverseTransformDirection(-f).XZOnly().normalized).eulerAngles.y);
                }
            foreach (var anchor in Object.FindObjectsOfType<Anchor_Stationary>())
                if ((position - anchor.transform.position).sqrMagnitude <= use && (!anchor.remoteControlled || EngineControls.AllEngineControls.Count == 0) && anchor.CanUse && anchor.AtBottom)
                    Traverse.Create(anchor).Method("WeighAnchor").GetValue();
        }
        public static void TryStopPropulsion(this Transform transform)
        {
            var position = transform.position;
            var flag = position.IsNearbyEngineControl();
            var use = Player.UseDistance * Player.UseDistance * Main.reach;
            foreach (var motor in Object.FindObjectsOfType<MotorWheel>())
                if ((flag || (position - motor.transform.position).sqrMagnitude <= use) && motor.engineSwitchOn)
                    motor.OnButtonPressed_ToggleEngine();
            foreach (var sail in Object.FindObjectsOfType<Sail>())
                if ((position - sail.transform.position).sqrMagnitude <= use && sail.open)
                    Traverse.Create(sail).Method("Close").GetValue();
        }
        public static void TryDropAnchors(this Transform transform)
        {
            var position = transform.position;
            var flag = position.IsNearbyEngineControl(true);
            var use = Player.UseDistance * Player.UseDistance * Main.reach;
            foreach (var anchor in Object.FindObjectsOfType<Anchor_Stationary>())
                if ((position - anchor.transform.position).sqrMagnitude <= use && (!anchor.remoteControlled || EngineControls.AllEngineControls.Count == 0) && anchor.CanUse && !anchor.AtBottom)
                    Traverse.Create(anchor).Method("DropAnchor").GetValue();
        }

        public static void Rotate(this SteeringWheel wheel, float axis)
        {
            if (Raft_Network.IsHost)
                Traverse.Create(wheel).Method("Rotate", axis).GetValue();
            else
                ComponentManager<Raft_Network>.Value.SendP2P(ComponentManager<Raft_Network>.Value.HostID, new Message_SteeringWheel_Rotate(Messages.SteeringWheelRotate, ComponentManager<Raft_Network>.Value.NetworkIDManager, wheel.ObjectIndex, axis), EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
        }
        public static void SetRotation(this Sail sail, float angle)
        {
            var axis = angle - sail.LocalRotation;
            if (Raft_Network.IsHost)
                Traverse.Create(sail).Method("Rotate", axis).GetValue();
            else
                ComponentManager<Raft_Network>.Value.SendP2P(ComponentManager<Raft_Network>.Value.HostID, new Message_Sail_Rotate(Messages.Sail_Rotate, sail, axis), EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
        }
        public static Y[] Cast<X, Y>(this X[] collection, Func<X, Y> convert)
        {
            var a = new Y[collection.Length];
            for (int i = 0; i < a.Length; i++)
                a[i] = convert(collection[i]);
            return a;
        }
        public static float ParseFloat(this string value, float EmptyFallback = 1)
        {
            if (string.IsNullOrWhiteSpace(value))
                return EmptyFallback;
            if (value.Contains(",") && !value.Contains("."))
                value = value.Replace(',', '.');
            var c = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfoByIetfLanguageTag("en-NZ");
            Exception e = null;
            float r = 0;
            try
            {
                r = float.Parse(value);
            }
            catch (Exception e2)
            {
                e = e2;
            }
            CultureInfo.CurrentCulture = c;
            if (e != null)
                throw e;
            return r;
        }
    }

    class KeyboardButtons : MonoBehaviour
    {
        static MethodInfo _bp = AccessTools.Method(typeof(InteractableButton), "ButtonPressed");
        static void ButtonPressed(InteractableButton button) => _bp.Invoke(button, new object[0]);
        public List<ConditionalButton> buttons = new List<ConditionalButton>();
        public Vector3 cameraOffset;
        public Quaternion cameraRotation;
        public bool takeCamera = true;
        public float buttonCooldown = 0;
        bool active = false;
        bool wasThirdPerson;
        bool wasActive;
        public bool waitForButtonRelease = false;
        void Awake()
        {
            GetComponent<InteractableButton>().OnButtonPressed += () => SetState(true);
        }
        public void SetState(bool Active)
        {
            if (active == Active)
                return;
            active = Active;
            var player = ComponentManager<Network_Player>.Value;
            player.PersonController.IsMovementFree = !active;
            player.PickupScript.enabled = !active;
            player.GetComponentInChildren<RemovePlaceables>().enabled = !active;
            if (takeCamera)
            {
                player.cameraBobAnimator.enabled = !active;
                player.PlayerScript.SetMouseLookScripts(!active);
                if (active)
                {
                    wasThirdPerson = player.currentModel.thirdPersonSettings.ThirdPersonModel;
                    player.currentModel.thirdPersonSettings.ThirdPersonModel = true;
                    Camera.main.transform.SetParent(transform);
                    Camera.main.transform.localPosition = cameraOffset;
                    Camera.main.transform.localRotation = cameraRotation;
                }
                else
                {
                    player.currentModel.thirdPersonSettings.ThirdPersonModel = wasThirdPerson;
                    Camera.main.transform.SetParent(player.currentModel.cameraHolder, false);
                }
            }
        }
        void Update()
        {
            foreach (var b in buttons)
            {
                if (b.cooldown > 0)
                {
                    b.cooldown -= Time.deltaTime;
                    if (b.cooldown > 0)
                        continue;
                }
                if (active && b.condition())
                {
                    if (!((b.releaseOverride == null ? waitForButtonRelease : b.releaseOverride.Value) && b.wasPressed))
                    {
                        if (b.button)
                            ButtonPressed(b.button);
                        b.onPress?.Invoke();
                        b.cooldown += (b.cooldownOverride == null ? buttonCooldown : b.cooldownOverride.Value);
                    }
                    b.wasPressed = true;
                }
                else
                    b.wasPressed = false;
            }
            if (wasActive && active && MyInput.GetButtonDown("Interact"))
                SetState(false);
            if (active)
            {
                int i = 1;
                foreach (var b in buttons)
                    if (b.buttonKey != null)
                        ComponentManager<DisplayTextManager>.Value.ShowText(b.showText, MyInput.Keybinds[b.buttonKey].MainKey, i++, 0, false);
            }
            else if (wasActive)
            {
                int i = 1;
                foreach (var b in buttons)
                    if (b.buttonKey != null)
                        ComponentManager<DisplayTextManager>.Value.HideDisplayTexts(i++);
            }
            wasActive = active;
        }
        void OnDestroy()
        {
            SetState(false);
        }
    }

    class ConditionalButton
    {
        public Func<bool> condition;
        public InteractableButton button;
        public Action onPress;
        public float cooldown;
        public string buttonKey = null;
        public string showText = null;
        public bool wasPressed = false;
        public float? cooldownOverride = null;
        public bool? releaseOverride = null;
        public ConditionalButton(Func<bool> Condition, InteractableButton Button) : this(Condition, Button, null, null, null) { }
        public ConditionalButton(Func<bool> Condition, InteractableButton Button, string ButtonKey, string ShowText) : this(Condition, Button, null, ButtonKey, ShowText) { }
        public ConditionalButton(Func<bool> Condition, Action PressAction) : this(Condition, null, PressAction, null, null) { }
        public ConditionalButton(Func<bool> Condition, Action PressAction, string ButtonKey, string ShowText) : this(Condition, null, PressAction, ButtonKey, ShowText) { }
        public ConditionalButton(Func<bool> Condition, InteractableButton Button, Action PressAction, string ButtonKey, string ShowText)
        {
            condition = Condition;
            button = Button;
            onPress = PressAction;
            cooldown = 0;
            if (ButtonKey != null && MyInput.Keybinds.ContainsKey(ButtonKey) && ShowText != null)
            {
                buttonKey = ButtonKey;
                showText = ShowText;
            }
        }
    }

    [HarmonyPatch(typeof(Landmark), "Initialize")]
    public class Patch_IslandSpawn
    {
        public static void Prefix(Landmark __instance, bool ___initialized)
        {
            if (!___initialized && __instance.uniqueLandmarkIndex == 50)
            {
                var panel = __instance.transform.Find("Offset/TangaroaLogic/Tangaroa_ClawChallenge/Claw_ControlBoard");
                var button_obj = panel.Find("Button_Claw");
                var button_claw = button_obj.GetComponent<InteractableButton_Networked_ClawChallenge>();
                var button_up = panel.Find("Button_Up").GetComponent<InteractableButton_Networked_ClawChallenge>();
                var button_down = panel.Find("Button_Down").GetComponent<InteractableButton_Networked_ClawChallenge>();
                var button_left = panel.Find("Button_Left").GetComponent<InteractableButton_Networked_ClawChallenge>();
                var button_right = panel.Find("Button_Right").GetComponent<InteractableButton_Networked_ClawChallenge>();
                var new_button = new GameObject("Button_PanelInteract");
                new_button.layer = button_obj.gameObject.layer;
                new_button.tag = button_obj.tag;
                new_button.transform.SetParent(button_obj.parent);
                new_button.transform.localPosition = button_obj.localPosition;
                new_button.transform.localRotation = button_obj.localRotation;
                new_button.transform.localScale = button_obj.localScale;
                var button_panel = new_button.AddComponent<InteractableButton>();
                var buttons = new_button.AddComponent<KeyboardButtons>();
                button_panel.localizationTerm = button_claw.localizationTerm;
                Traverse.Create(button_panel).Field("keybindName").SetValue(Traverse.Create(button_claw).Field("keybindName").GetValue());
                buttons.buttonCooldown = 0.1f;
                buttons.cameraRotation = Quaternion.Euler(0, 180, 180);
                buttons.cameraOffset = new Vector3(-3, -4, 0);
                buttons.buttons.Add(new ConditionalButton(() => MyInput.GetAxis("Walk") > 0, button_up));
                buttons.buttons.Add(new ConditionalButton(() => MyInput.GetAxis("Walk") < 0, button_down));
                buttons.buttons.Add(new ConditionalButton(() => MyInput.GetAxis("Strafe") > 0, button_right));
                buttons.buttons.Add(new ConditionalButton(() => MyInput.GetAxis("Strafe") < 0, button_left));
                buttons.buttons.Add(new ConditionalButton(() => MyInput.GetButton("Jump"), button_claw, "Jump", "Pickup / Drop"));
                var collider = new_button.AddComponent<BoxCollider>();
                var c2 = button_obj.GetComponent<BoxCollider>();
                var s = c2.size;
                s.Scale(new Vector3(5, 5, 2));
                collider.size = s;
                collider.center = c2.center;
                new_button.AddComponent<RaycastInteractable>().InitRaycastables();
            }
        }
    }

    [HarmonyPatch(typeof(Reciever), "OnBlockPlaced")]
    public class Patch_PlaceReciever
    {
        public static void Postfix(Reciever __instance)
        {
            var bc = __instance.GetComponentsInChildren<InteractableButton>();
            var button_up = bc.First((x) => x.name == "Button_Up");
            var button_down = bc.First((x) => x.name == "Button_Down");
            var button_right = bc.First((x) => x.name == "Button_Cycle");
            var new_button = new GameObject("Button_RecieverInteract");
            new_button.layer = button_up.gameObject.layer;
            new_button.tag = button_up.tag;
            new_button.transform.SetParent(button_up.transform.parent);
            new_button.transform.localPosition = button_up.transform.localPosition;
            new_button.transform.localRotation = button_up.transform.localRotation;
            new_button.transform.localScale = button_up.transform.localScale;
            var button_panel = new_button.AddComponent<InteractableButton>();
            var buttons = new_button.AddComponent<KeyboardButtons>();
            button_panel.localizationTerm = "Game/Use";
            Traverse.Create(button_panel).Field("keybindName").SetValue(Traverse.Create(button_up).Field("keybindName").GetValue());
            buttons.waitForButtonRelease = true;
            buttons.cameraRotation = Quaternion.Euler(-45, 180, 0);
            buttons.cameraOffset = new Vector3(-0.2f, -0.2f, 0.3f);
            buttons.buttons.Add(new ConditionalButton(() => MyInput.GetAxis("Walk") > 0, button_up));
            buttons.buttons.Add(new ConditionalButton(() => MyInput.GetAxis("Walk") < 0, button_down));
            buttons.buttons.Add(new ConditionalButton(() => MyInput.GetAxis("Strafe") > 0, button_right));
            var collider = new_button.AddComponent<BoxCollider>();
            var s = button_up.GetComponent<BoxCollider>().size;
            s.Scale(new Vector3(5, 5, 5));
            collider.size = s;
            collider.center = new Vector3(-0.18f, 0.08f, 0.18f);
            new_button.AddComponent<RaycastInteractable>().InitRaycastables();
        }
    }

    [HarmonyPatch(typeof(SteeringWheel), "OnBlockPlaced")]
    public class Patch_PlaceSteeringWheel
    {
        public static void Postfix(SteeringWheel __instance)
        {
            var wheel = __instance.GetComponent<BoxCollider>();
            var new_button = new GameObject("Button_WheelInteract");
            new_button.layer = wheel.gameObject.layer;
            new_button.tag = wheel.tag;
            new_button.transform.SetParent(wheel.transform, false);
            var button_panel = new_button.AddComponent<InteractableButton>();
            var buttons = new_button.AddComponent<KeyboardButtons>();
            button_panel.localizationTerm = "Game/Use";
            Traverse.Create(button_panel).Field("keybindName").SetValue("Interact");
            buttons.waitForButtonRelease = true;
            buttons.takeCamera = false;
            buttons.buttons.Add(new ConditionalButton(() => MyInput.GetAxis("Walk") > 0, () => __instance.transform.TrySetMovingForward()));
            buttons.buttons.Add(new ConditionalButton(() => MyInput.GetAxis("Walk") < 0, () => __instance.transform.TrySetMovingForward(true)));
            buttons.buttons.Add(new ConditionalButton(() => MyInput.GetAxis("Strafe") != 0, () => __instance.Rotate(MyInput.GetAxis("Strafe") * 2)) { releaseOverride = false });
            buttons.buttons.Add(new ConditionalButton(() => MyInput.GetButtonDown("Sprint"), () => __instance.transform.TryStopPropulsion(), "Sprint", "Stop Engines"));
            buttons.buttons.Add(new ConditionalButton(() => MyInput.GetButtonDown("Jump"), () => __instance.transform.TryDropAnchors(), "Jump", "Drop Anchors"));
            var collider = new_button.AddComponent<BoxCollider>();
            var s = wheel.size;
            s.Scale(new Vector3(1.1f, 1.1f, 1.1f));
            collider.size = s;
            collider.center = wheel.center;
            new_button.AddComponent<RaycastInteractable>().InitRaycastables();
        }
    }
}