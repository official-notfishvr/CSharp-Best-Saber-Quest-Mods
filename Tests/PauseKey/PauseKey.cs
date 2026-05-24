using CoreMod;
using GlobalNamespace;
using UnityEngine;

namespace PauseKey;

[Mod("com.csharp.quest.pausekey", "0.1.0")]
public static class PauseKeyMod
{
    private const int DefaultBinding = 0;
    private const int LeftTriggerBinding = 1;
    private const int RightTriggerBinding = 2;
    private const int LeftGripBinding = 3;
    private const int RightGripBinding = 4;
    private const int LeftPrimaryBinding = 5;
    private const int RightPrimaryBinding = 6;
    private const int LeftSecondaryBinding = 7;
    private const int RightSecondaryBinding = 8;
    private const int LeftStickBinding = 9;
    private const int RightStickBinding = 10;

    private static bool _pressedLastFrame;

    [Config(Description = "Enable PauseKey on Quest", DefaultValue = true)]
    public static bool Enabled { get; set; } = true;

    [Config(Description = "0 default menu, 1/2 trigger, 3/4 grip, 5/6 primary, 7/8 secondary, 9/10 stick", DefaultValue = DefaultBinding)]
    public static int PauseBinding { get; set; } = DefaultBinding;

    [Config(Description = "Trigger press threshold when using a trigger binding", DefaultValue = 0.75f)]
    public static float TriggerThreshold { get; set; } = 0.75f;

    [Config(Description = "Thumbstick magnitude threshold when using a stick binding", DefaultValue = 0.85f)]
    public static float ThumbstickThreshold { get; set; } = 0.85f;

    [Hook(typeof(UnityXRHelper), nameof(UnityXRHelper.GetMenuButton))]
    public static bool OnGetMenuButton(UnityXRHelper self)
    {
        if (!Enabled)
            return OnGetMenuButton(self);

        if (PauseBinding == DefaultBinding)
            return OnGetMenuButton(self);

        return GetBindingState(self, PauseBinding);
    }

    [Hook(typeof(UnityXRHelper), nameof(UnityXRHelper.GetMenuButtonDown))]
    public static bool OnGetMenuButtonDown(UnityXRHelper self)
    {
        if (!Enabled)
            return OnGetMenuButtonDown(self);

        if (PauseBinding == DefaultBinding)
            return OnGetMenuButtonDown(self);

        var isPressed = GetBindingState(self, PauseBinding);

        var pressedThisFrame = false;
        if (isPressed)
            pressedThisFrame = !_pressedLastFrame;

        _pressedLastFrame = isPressed;
        return pressedThisFrame;
    }

    [Hook(typeof(UnityXRHelper), nameof(UnityXRHelper.OnApplicationPause), Phase = HookPhase.Postfix)]
    public static void OnApplicationPausePostfix(UnityXRHelper self, bool pauseStatus)
    {
        if (!pauseStatus)
            return;

        _pressedLastFrame = false;
    }

    private static bool GetBindingState(UnityXRHelper self, int binding)
    {
        if (binding == LeftTriggerBinding)
        {
            if (self._leftController == null)
                return false;

            return self.GetTriggerValue(self._leftController.node) >= TriggerThreshold;
        }

        if (binding == RightTriggerBinding)
        {
            if (self._rightController == null)
                return false;

            return self.GetTriggerValue(self._rightController.node) >= TriggerThreshold;
        }

        if (binding == LeftStickBinding)
        {
            if (self._leftController == null)
                return false;

            var thumbstick = self.GetThumbstickValue(self._leftController.node);
            return Vector2.SqrMagnitude(thumbstick) >= ThumbstickThreshold * ThumbstickThreshold;
        }

        if (binding == RightStickBinding)
        {
            if (self._rightController == null)
                return false;

            var thumbstick = self.GetThumbstickValue(self._rightController.node);
            return Vector2.SqrMagnitude(thumbstick) >= ThumbstickThreshold * ThumbstickThreshold;
        }

        return false;
    }
}
