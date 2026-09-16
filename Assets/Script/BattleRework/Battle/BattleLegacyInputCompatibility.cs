using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// D 단계 입력 마이그레이션의 안전망입니다.
///
/// Assembly-CSharp의 전역 Input 심볼을 New Input System 기반으로 제공하여,
/// 아직 남아 있는 소수의 `Input.GetKey* / mousePosition / GetAxis*` 호출이
/// ProjectSettings를 New Input System Only로 전환한 뒤에도 UnityEngine.Input을 호출하지 않게 합니다.
///
/// 신규 코드는 이 타입을 사용하지 말고 BattleInputRouter를 사용해야 합니다.
/// 이 파일은 남은 보정/디버그 레이어를 순차 흡수할 때 함께 제거할 수 있습니다.
/// </summary>
internal static class Input
{
    public static bool mousePresent => Mouse.current != null || Pointer.current != null;

    public static Vector3 mousePosition
    {
        get
        {
            Vector2 value = Pointer.current != null ? Pointer.current.position.ReadValue() : Vector2.zero;
            return new Vector3(value.x, value.y, 0f);
        }
    }

    public static Vector2 mouseScrollDelta => Mouse.current != null ? Mouse.current.scroll.ReadValue() : Vector2.zero;

    public static bool anyKey
    {
        get
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.isPressed)
                return true;
            if (Mouse.current != null &&
                (Mouse.current.leftButton.isPressed || Mouse.current.rightButton.isPressed || Mouse.current.middleButton.isPressed))
                return true;
            return Gamepad.current != null && Gamepad.current.allControls.Exists(control => control is ButtonControl button && button.isPressed);
        }
    }

    public static bool anyKeyDown
    {
        get
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                return true;
            if (Mouse.current != null &&
                (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame || Mouse.current.middleButton.wasPressedThisFrame))
                return true;
            return Gamepad.current != null && Gamepad.current.allControls.Exists(control => control is ButtonControl button && button.wasPressedThisFrame);
        }
    }

    public static bool GetKey(KeyCode key) => ResolveButton(key)?.isPressed ?? false;
    public static bool GetKeyDown(KeyCode key) => ResolveButton(key)?.wasPressedThisFrame ?? false;
    public static bool GetKeyUp(KeyCode key) => ResolveButton(key)?.wasReleasedThisFrame ?? false;

    public static bool GetKey(string name)
    {
        return TryResolveNamedKey(name, out KeyCode key) && GetKey(key);
    }

    public static bool GetKeyDown(string name)
    {
        return TryResolveNamedKey(name, out KeyCode key) && GetKeyDown(key);
    }

    public static bool GetKeyUp(string name)
    {
        return TryResolveNamedKey(name, out KeyCode key) && GetKeyUp(key);
    }

    public static float GetAxis(string axisName) => GetAxisRaw(axisName);

    public static float GetAxisRaw(string axisName)
    {
        if (string.IsNullOrEmpty(axisName))
            return 0f;

        switch (axisName)
        {
            case "Horizontal":
                return ResolveMoveAxis(horizontal: true);
            case "Vertical":
                return ResolveMoveAxis(horizontal: false);
            case "Mouse X":
                return Mouse.current != null ? Mouse.current.delta.ReadValue().x : 0f;
            case "Mouse Y":
                return Mouse.current != null ? Mouse.current.delta.ReadValue().y : 0f;
            case "Mouse ScrollWheel":
                return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
            default:
                return 0f;
        }
    }

    public static bool GetButton(string buttonName) => ResolveNamedButton(buttonName)?.isPressed ?? false;
    public static bool GetButtonDown(string buttonName) => ResolveNamedButton(buttonName)?.wasPressedThisFrame ?? false;
    public static bool GetButtonUp(string buttonName) => ResolveNamedButton(buttonName)?.wasReleasedThisFrame ?? false;

    public static bool GetMouseButton(int button) => ResolveMouseButton(button)?.isPressed ?? false;
    public static bool GetMouseButtonDown(int button) => ResolveMouseButton(button)?.wasPressedThisFrame ?? false;
    public static bool GetMouseButtonUp(int button) => ResolveMouseButton(button)?.wasReleasedThisFrame ?? false;

    private static float ResolveMoveAxis(bool horizontal)
    {
        float keyboard = 0f;
        if (Keyboard.current != null)
        {
            if (horizontal)
            {
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) keyboard -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) keyboard += 1f;
            }
            else
            {
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) keyboard -= 1f;
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) keyboard += 1f;
            }
        }

        float gamepad = 0f;
        if (Gamepad.current != null)
        {
            Vector2 stick = Gamepad.current.leftStick.ReadValue();
            gamepad = horizontal ? stick.x : stick.y;
        }

        return Mathf.Abs(gamepad) > Mathf.Abs(keyboard) ? gamepad : keyboard;
    }

    private static ButtonControl ResolveNamedButton(string buttonName)
    {
        switch (buttonName)
        {
            case "Fire1":
                if (Mouse.current != null) return Mouse.current.leftButton;
                return Gamepad.current?.rightTrigger;
            case "Fire2":
                if (Mouse.current != null) return Mouse.current.rightButton;
                return Gamepad.current?.leftTrigger;
            case "Fire3":
                if (Mouse.current != null) return Mouse.current.middleButton;
                return Gamepad.current?.rightShoulder;
            case "Jump":
                if (Keyboard.current != null) return Keyboard.current.spaceKey;
                return Gamepad.current?.buttonSouth;
            case "Submit":
                if (Keyboard.current != null) return Keyboard.current.enterKey;
                return Gamepad.current?.buttonSouth;
            case "Cancel":
                if (Keyboard.current != null) return Keyboard.current.escapeKey;
                return Gamepad.current?.buttonEast;
            default:
                return null;
        }
    }

    private static ButtonControl ResolveMouseButton(int button)
    {
        if (Mouse.current == null)
            return null;

        return button switch
        {
            0 => Mouse.current.leftButton,
            1 => Mouse.current.rightButton,
            2 => Mouse.current.middleButton,
            3 => Mouse.current.forwardButton,
            4 => Mouse.current.backButton,
            _ => null
        };
    }

    private static bool TryResolveNamedKey(string name, out KeyCode key)
    {
        key = KeyCode.None;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        string normalized = name.Trim().Replace(" ", string.Empty);
        if (System.Enum.TryParse(normalized, true, out key))
            return true;

        switch (normalized.ToLowerInvariant())
        {
            case "enter": key = KeyCode.Return; return true;
            case "esc": key = KeyCode.Escape; return true;
            case "ctrl": key = KeyCode.LeftControl; return true;
            case "shift": key = KeyCode.LeftShift; return true;
            default: return false;
        }
    }

    private static ButtonControl ResolveButton(KeyCode key)
    {
        Keyboard keyboard = Keyboard.current;
        Gamepad gamepad = Gamepad.current;

        if (key >= KeyCode.JoystickButton0 && key <= KeyCode.JoystickButton19)
        {
            int index = key - KeyCode.JoystickButton0;
            if (gamepad == null)
                return null;

            return index switch
            {
                0 => gamepad.buttonSouth,
                1 => gamepad.buttonEast,
                2 => gamepad.buttonWest,
                3 => gamepad.buttonNorth,
                4 => gamepad.leftShoulder,
                5 => gamepad.rightShoulder,
                6 => gamepad.selectButton,
                7 => gamepad.startButton,
                8 => gamepad.leftStickButton,
                9 => gamepad.rightStickButton,
                _ => null
            };
        }

        if (keyboard == null)
            return null;

        return key switch
        {
            KeyCode.A => keyboard.aKey,
            KeyCode.B => keyboard.bKey,
            KeyCode.C => keyboard.cKey,
            KeyCode.D => keyboard.dKey,
            KeyCode.E => keyboard.eKey,
            KeyCode.F => keyboard.fKey,
            KeyCode.G => keyboard.gKey,
            KeyCode.H => keyboard.hKey,
            KeyCode.I => keyboard.iKey,
            KeyCode.J => keyboard.jKey,
            KeyCode.K => keyboard.kKey,
            KeyCode.L => keyboard.lKey,
            KeyCode.M => keyboard.mKey,
            KeyCode.N => keyboard.nKey,
            KeyCode.O => keyboard.oKey,
            KeyCode.P => keyboard.pKey,
            KeyCode.Q => keyboard.qKey,
            KeyCode.R => keyboard.rKey,
            KeyCode.S => keyboard.sKey,
            KeyCode.T => keyboard.tKey,
            KeyCode.U => keyboard.uKey,
            KeyCode.V => keyboard.vKey,
            KeyCode.W => keyboard.wKey,
            KeyCode.X => keyboard.xKey,
            KeyCode.Y => keyboard.yKey,
            KeyCode.Z => keyboard.zKey,

            KeyCode.Alpha0 => keyboard.digit0Key,
            KeyCode.Alpha1 => keyboard.digit1Key,
            KeyCode.Alpha2 => keyboard.digit2Key,
            KeyCode.Alpha3 => keyboard.digit3Key,
            KeyCode.Alpha4 => keyboard.digit4Key,
            KeyCode.Alpha5 => keyboard.digit5Key,
            KeyCode.Alpha6 => keyboard.digit6Key,
            KeyCode.Alpha7 => keyboard.digit7Key,
            KeyCode.Alpha8 => keyboard.digit8Key,
            KeyCode.Alpha9 => keyboard.digit9Key,

            KeyCode.LeftArrow => keyboard.leftArrowKey,
            KeyCode.RightArrow => keyboard.rightArrowKey,
            KeyCode.UpArrow => keyboard.upArrowKey,
            KeyCode.DownArrow => keyboard.downArrowKey,
            KeyCode.Space => keyboard.spaceKey,
            KeyCode.Return => keyboard.enterKey,
            KeyCode.KeypadEnter => keyboard.numpadEnterKey,
            KeyCode.Escape => keyboard.escapeKey,
            KeyCode.Tab => keyboard.tabKey,
            KeyCode.Backspace => keyboard.backspaceKey,
            KeyCode.Delete => keyboard.deleteKey,
            KeyCode.Home => keyboard.homeKey,
            KeyCode.End => keyboard.endKey,
            KeyCode.PageUp => keyboard.pageUpKey,
            KeyCode.PageDown => keyboard.pageDownKey,
            KeyCode.LeftShift => keyboard.leftShiftKey,
            KeyCode.RightShift => keyboard.rightShiftKey,
            KeyCode.LeftControl => keyboard.leftCtrlKey,
            KeyCode.RightControl => keyboard.rightCtrlKey,
            KeyCode.LeftAlt => keyboard.leftAltKey,
            KeyCode.RightAlt => keyboard.rightAltKey,

            KeyCode.F1 => keyboard.f1Key,
            KeyCode.F2 => keyboard.f2Key,
            KeyCode.F3 => keyboard.f3Key,
            KeyCode.F4 => keyboard.f4Key,
            KeyCode.F5 => keyboard.f5Key,
            KeyCode.F6 => keyboard.f6Key,
            KeyCode.F7 => keyboard.f7Key,
            KeyCode.F8 => keyboard.f8Key,
            KeyCode.F9 => keyboard.f9Key,
            KeyCode.F10 => keyboard.f10Key,
            KeyCode.F11 => keyboard.f11Key,
            KeyCode.F12 => keyboard.f12Key,
            _ => null
        };
    }
}
