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
    private const int LeftThumbstickBinding = 3;
    private const int RightThumbstickBinding = 4;

    private static bool _pressedLastFrame;

    [Config(Description = "Enable PauseKey on Quest", DefaultValue = true)]
    public static bool Enabled { get; set; } = true;

    [Config(Description = "0 default menu button, 1 left trigger, 2 right trigger, 3 left thumbstick tilt, 4 right thumbstick tilt", DefaultValue = RightThumbstickBinding)]
    public static int PauseBinding { get; set; } = RightThumbstickBinding;

    [Config(Description = "Trigger press threshold when using a trigger binding", DefaultValue = 0.75f)]
    public static float TriggerThreshold { get; set; } = 0.75f;

    [Config(Description = "Thumbstick magnitude threshold when using a thumbstick binding", DefaultValue = 0.85f)]
    public static float ThumbstickThreshold { get; set; } = 0.85f;

    [Hook(typeof(UnityXRHelper), nameof(UnityXRHelper.GetMenuButton))]
    public static bool OnGetMenuButton(UnityXRHelper self)
    {
        if (!Enabled)
            return OnGetMenuButton(self);

        if (PauseBinding == DefaultBinding)
            return OnGetMenuButton(self);

        if (PauseBinding == LeftTriggerBinding)
        {
            if (self._leftController == null)
                return false;

            return self.GetTriggerValue(self._leftController.node) >= TriggerThreshold;
        }

        if (PauseBinding == RightTriggerBinding)
        {
            if (self._rightController == null)
                return false;

            return self.GetTriggerValue(self._rightController.node) >= TriggerThreshold;
        }

        if (PauseBinding == LeftThumbstickBinding)
        {
            if (self._leftController == null)
                return false;

            var thumbstick = self.GetThumbstickValue(self._leftController.node);
            return Vector2.SqrMagnitude(thumbstick) >= ThumbstickThreshold * ThumbstickThreshold;
        }

        if (PauseBinding == RightThumbstickBinding)
        {
            if (self._rightController == null)
                return false;

            var thumbstick = self.GetThumbstickValue(self._rightController.node);
            return Vector2.SqrMagnitude(thumbstick) >= ThumbstickThreshold * ThumbstickThreshold;
        }

        return false;
    }

    [Hook(typeof(UnityXRHelper), nameof(UnityXRHelper.GetMenuButtonDown))]
    public static bool OnGetMenuButtonDown(UnityXRHelper self)
    {
        if (!Enabled)
            return OnGetMenuButtonDown(self);

        if (PauseBinding == DefaultBinding)
            return OnGetMenuButtonDown(self);

        var isPressed = false;

        if (PauseBinding == LeftTriggerBinding)
        {
            if (self._leftController != null)
                isPressed = self.GetTriggerValue(self._leftController.node) >= TriggerThreshold;
        }

        if (PauseBinding == RightTriggerBinding)
        {
            if (self._rightController != null)
                isPressed = self.GetTriggerValue(self._rightController.node) >= TriggerThreshold;
        }

        if (PauseBinding == LeftThumbstickBinding)
        {
            if (self._leftController != null)
            {
                var thumbstick = self.GetThumbstickValue(self._leftController.node);
                isPressed = Vector2.SqrMagnitude(thumbstick) >= ThumbstickThreshold * ThumbstickThreshold;
            }
        }

        if (PauseBinding == RightThumbstickBinding)
        {
            if (self._rightController != null)
            {
                var thumbstick = self.GetThumbstickValue(self._rightController.node);
                isPressed = Vector2.SqrMagnitude(thumbstick) >= ThumbstickThreshold * ThumbstickThreshold;
            }
        }

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
}
