﻿using System.ComponentModel;
using System.Text.Json.Serialization;
using Avalonia.Input;
using Everywhere.AI;
using Everywhere.Chat.Permissions;
using Everywhere.Database;
using Everywhere.Interop;
using Everywhere.Storage;
using Lucide.Avalonia;
using Microsoft.SemanticKernel;
using ZLinq;

namespace Everywhere.Chat.Plugins;

public class VisualTreePlugin : BuiltInChatPlugin
{
    public override DynamicResourceKeyBase HeaderKey { get; } = new DynamicResourceKey(LocaleKey.NativeChatPlugin_VisualTree_Header);
    public override DynamicResourceKeyBase DescriptionKey { get; } = new DynamicResourceKey(LocaleKey.NativeChatPlugin_VisualTree_Description);
    public override LucideIconKind? Icon => LucideIconKind.Component;

    private readonly IBlobStorage _blobStorage;
    private readonly IVisualElementContext _visualElementContext;

    public VisualTreePlugin(IBlobStorage blobStorage, IVisualElementContext visualElementContext) : base("visual_tree")
    {
        _blobStorage = blobStorage;
        _visualElementContext = visualElementContext;
        _functions.Add(
            new NativeChatFunction(
                CaptureVisualElementByIdAsync,
                ChatFunctionPermissions.ScreenRead));
        _functions.Add(
            new NativeChatFunction(
                CaptureFullScreenAsync,
                ChatFunctionPermissions.ScreenRead));
        _functions.Add(
            new NativeChatFunction(
                ExecuteVisualActionQueueAsync,
                ChatFunctionPermissions.ScreenAccess));
    }

    /// <summary>
    /// When LLM does not support image input or image feature is disabled, and there is no visual element in the chat context, hide this plugin.
    /// </summary>
    /// <param name="chatContext"></param>
    /// <param name="customAssistant"></param>
    /// <returns></returns>
    public override IEnumerable<ChatFunction> SnapshotFunctions(ChatContext chatContext, CustomAssistant customAssistant) =>
        customAssistant.IsImageInputSupported.ActualValue is not true ||
        chatContext.VisualElements.Count == 0 ?
            [] :
            base.SnapshotFunctions(chatContext, customAssistant);

    [KernelFunction("capture_visual_element_by_id")]
    [Description("Captures a screenshot of the specified visual element by Id. Use when XML content is inaccessible or element is image-like.")]
    [DynamicResourceKey(LocaleKey.NativeChatPlugin_VisualTree_CaptureVisualElementById_Header)]
    private Task<ChatFileAttachment> CaptureVisualElementByIdAsync([FromKernelServices] ChatContext chatContext, int elementId)
    {
        return CaptureVisualElementAsync(ResolveVisualElement(chatContext, elementId, nameof(elementId)));
    }

    [KernelFunction("capture_full_screen")]
    [Description("Captures a screenshot of the entire screen. Use when no specific visual element is available.")]
    [DynamicResourceKey(LocaleKey.NativeChatPlugin_VisualTree_CaptureFullScreen_Header)]
    private Task<ChatFileAttachment> CaptureFullScreenAsync()
    {
        var visualElement = _visualElementContext.ElementFromPointer(PickElementMode.Screen);
        if (visualElement is null)
        {
            throw new InvalidOperationException("No screen is available to capture.");
        }

        return CaptureVisualElementAsync(visualElement);
    }

    private async Task<ChatFileAttachment> CaptureVisualElementAsync(IVisualElement visualElement)
    {
        var bitmap = await visualElement.CaptureAsync();

        BlobEntity blob;
        using (var stream = new MemoryStream())
        {
            bitmap.Save(stream, 100);
            blob = await _blobStorage.StorageBlobAsync(stream, "image/png");
        }

        return new ChatFileAttachment(
            new DynamicResourceKey(string.Empty),
            blob.LocalPath,
            blob.Sha256,
            blob.MimeType);
    }

    [KernelFunction("execute_visual_action_queue")]
    [Description(
        "Executes a reliable UI automation action queue. Supports clicking elements, entering text, sending shortcuts (e.g., Ctrl+V), and waiting without simulating pointer input. " +
        "Useful for automating stable interactions, even when the target window is minimized.")]
    [DynamicResourceKey(LocaleKey.NativeChatPlugin_VisualTree_ExecuteVisualActionQueue_Header)]
    private async static Task<string> ExecuteVisualActionQueueAsync(
        [FromKernelServices] ChatContext chatContext,
        VisualActionStep[] actions,
        CancellationToken cancellationToken = default)
    {
        if (actions == null || actions.Length == 0)
        {
            throw new ArgumentException("Action queue cannot be empty.", nameof(actions));
        }

        var index = 0;
        foreach (var step in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;

            try
            {
                switch (step.Type)
                {
                    case VisualActionType.Click:
                    {
                        var element = ResolveVisualElement(chatContext, step.EnsureElementId(), nameof(actions));
                        element.Invoke();
                        break;
                    }
                    case VisualActionType.SetText:
                    {
                        var element = ResolveVisualElement(chatContext, step.EnsureElementId(), nameof(actions));
                        element.SetText(step.Text ?? string.Empty);
                        break;
                    }
                    case VisualActionType.SendKey:
                    {
                        var element = ResolveVisualElement(chatContext, step.EnsureElementId(), nameof(actions));
                        var shortcut = step.ResolveShortcut();
                        element.SendShortcut(shortcut);
                        break;
                    }
                    case VisualActionType.Wait:
                    {
                        var delay = step.EnsureDelayMs();
                        if (delay < 0)
                        {
                            throw new ArgumentException($"Delay must be non-negative for wait actions (step {index}).", nameof(actions));
                        }

                        await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken).ConfigureAwait(false);
                        break;
                    }
                    default:
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(actions),
                            $"Unsupported action type '{step.Type}' at step {index}.");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not ArgumentOutOfRangeException)
            {
                throw new InvalidOperationException($"Action #{index} ({step.Type}) failed: {ex.Message}", ex);
            }
        }

        return $"Executed {actions.Length} action(s).";
    }

    private static IVisualElement ResolveVisualElement(ChatContext chatContext, int elementId, string? argumentName = null)
    {
        if (!chatContext.VisualElements.TryGetValue(elementId, out var visualElement))
        {
            throw new ArgumentException(
                $"Visual element with id '{elementId}' is not found or has been destroyed.",
                argumentName ?? nameof(elementId));
        }

        return visualElement;
    }

    /// <summary>
    /// Retries parsing the action type from string.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<VisualActionType>))]
    public enum VisualActionType
    {
        Click,
        SetText,
        Wait,
        SendKey
    }

    /// <summary>
    /// Represents a single action in the visual automation queue.
    /// </summary>
    public sealed record VisualActionStep
    {
        /// <summary>
        /// The type of action
        /// </summary>
        [Description("Type of action")]
        public VisualActionType Type { get; init; }

        /// <summary>
        /// The ID of the target visual element
        /// </summary>
        [Description("ID of the target visual element (optional for wait action)")]
        public int? ElementId { get; init; }

        /// <summary>
        /// The text to input (for SetText actions)
        /// </summary>
        [Description("Text for SetText action")]
        public string? Text { get; init; }

        /// <summary>
        /// The virtual key code to send (e.g., VK_RETURN, VK_A). Required for send_shortcut actions.
        /// </summary>
        [Description("Virtual key code (e.g., VK_RETURN, VK_A) for SendKey action")]
        public string? Key { get; init; }

        /// <summary>
        /// Modifier keys (e.g., VK_CONTROL, VK_SHIFT) for send_shortcut actions
        /// </summary>
        [Description("Modifier virtual key codes (e.g., VK_CONTROL, VK_SHIFT) for send_shortcut action, can contain multiple values combined with +")]
        public string? Modifiers { get; init; }

        /// <summary>
        /// Delay in milliseconds (for wait actions)
        /// </summary>
        [Description("Delay in milliseconds for wait action")]
        public int? DelayMs { get; init; }

        public int EnsureElementId() => ElementId ?? throw new ArgumentException($"{nameof(ElementId)} is required for this action.");

        public int EnsureDelayMs() => DelayMs ?? throw new ArgumentException($"{nameof(DelayMs)} is required for wait actions.");

        public KeyboardShortcut ResolveShortcut()
        {
            if (string.IsNullOrWhiteSpace(Key))
            {
                throw new ArgumentException("Key is required for SendKey actions.");
            }

            var key = ParseVirtualKey(Key);

            var modifiers = KeyModifiers.None;
            if (!Modifiers.IsNullOrEmpty())
            {
                var parts = Modifiers
                    .Split(' ', '+', ',', ';')
                    .AsValueEnumerable()
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim());
                foreach (var part in parts)
                {
                    modifiers |= part switch
                    {
                        "VK_SHIFT" => KeyModifiers.Shift,
                        "VK_CONTROL" => KeyModifiers.Control,
                        "VK_MENU" => KeyModifiers.Alt,
                        "VK_LWIN" or "VK_RWIN" => KeyModifiers.Meta,
                        _ => throw new ArgumentException($"Invalid modifier key: '{part}'.")
                    };
                }
            }

            return new KeyboardShortcut(key, modifiers);
        }

        private static Key ParseVirtualKey(string virtualKey)
        {
            virtualKey = virtualKey.Trim().ToUpperInvariant();
            if (!virtualKey.StartsWith("VK_")) virtualKey = "VK_" + virtualKey;
            if (!Enum.TryParse<VIRTUAL_KEY>(virtualKey, out var vk))
            {
                throw new ArgumentException($"Invalid virtual key code: '{virtualKey}'.");
            }

            return vk switch
            {
                // Alphanumeric keys
                >= VIRTUAL_KEY.VK_A and <= VIRTUAL_KEY.VK_Z => (Key)((int)Avalonia.Input.Key.A + ((int)vk - (int)VIRTUAL_KEY.VK_A)),

                // Function keys
                >= VIRTUAL_KEY.VK_F1 and <= VIRTUAL_KEY.VK_F24 => (Key)((int)Avalonia.Input.Key.F1 + ((int)vk - (int)VIRTUAL_KEY.VK_F1)),

                // Number keys (top row)
                >= VIRTUAL_KEY.VK_0 and <= VIRTUAL_KEY.VK_9 => (Key)((int)Avalonia.Input.Key.D0 + ((int)vk - (int)VIRTUAL_KEY.VK_0)),

                // Numpad keys
                >= VIRTUAL_KEY.VK_NUMPAD0 and <= VIRTUAL_KEY.VK_NUMPAD9 => (Key)((int)Avalonia.Input.Key.NumPad0 + ((int)vk - (int)VIRTUAL_KEY.VK_NUMPAD0)),

                // Special keys
                VIRTUAL_KEY.VK_BACK => Avalonia.Input.Key.Back,
                VIRTUAL_KEY.VK_TAB => Avalonia.Input.Key.Tab,
                VIRTUAL_KEY.VK_RETURN => Avalonia.Input.Key.Return,
                VIRTUAL_KEY.VK_ESCAPE => Avalonia.Input.Key.Escape,
                VIRTUAL_KEY.VK_SPACE => Avalonia.Input.Key.Space,
                VIRTUAL_KEY.VK_PRIOR => Avalonia.Input.Key.PageUp,
                VIRTUAL_KEY.VK_NEXT => Avalonia.Input.Key.PageDown,
                VIRTUAL_KEY.VK_END => Avalonia.Input.Key.End,
                VIRTUAL_KEY.VK_HOME => Avalonia.Input.Key.Home,
                VIRTUAL_KEY.VK_LEFT => Avalonia.Input.Key.Left,
                VIRTUAL_KEY.VK_UP => Avalonia.Input.Key.Up,
                VIRTUAL_KEY.VK_RIGHT => Avalonia.Input.Key.Right,
                VIRTUAL_KEY.VK_DOWN => Avalonia.Input.Key.Down,
                VIRTUAL_KEY.VK_SNAPSHOT => Avalonia.Input.Key.PrintScreen,
                VIRTUAL_KEY.VK_INSERT => Avalonia.Input.Key.Insert,
                VIRTUAL_KEY.VK_DELETE => Avalonia.Input.Key.Delete,
                VIRTUAL_KEY.VK_HELP => Avalonia.Input.Key.Help,
                VIRTUAL_KEY.VK_LWIN => Avalonia.Input.Key.LWin,
                VIRTUAL_KEY.VK_RWIN => Avalonia.Input.Key.RWin,
                VIRTUAL_KEY.VK_APPS => Avalonia.Input.Key.Apps,
                VIRTUAL_KEY.VK_SLEEP => Avalonia.Input.Key.Sleep,
                VIRTUAL_KEY.VK_MULTIPLY => Avalonia.Input.Key.Multiply,
                VIRTUAL_KEY.VK_ADD => Avalonia.Input.Key.Add,
                VIRTUAL_KEY.VK_SEPARATOR => Avalonia.Input.Key.Separator,
                VIRTUAL_KEY.VK_SUBTRACT => Avalonia.Input.Key.Subtract,
                VIRTUAL_KEY.VK_DECIMAL => Avalonia.Input.Key.Decimal,
                VIRTUAL_KEY.VK_DIVIDE => Avalonia.Input.Key.Divide,
                VIRTUAL_KEY.VK_SHIFT => Avalonia.Input.Key.LeftShift,
                VIRTUAL_KEY.VK_CONTROL => Avalonia.Input.Key.LeftCtrl,
                VIRTUAL_KEY.VK_MENU => Avalonia.Input.Key.LeftAlt,
                VIRTUAL_KEY.VK_PAUSE => Avalonia.Input.Key.Pause,
                VIRTUAL_KEY.VK_CAPITAL => Avalonia.Input.Key.CapsLock,
                VIRTUAL_KEY.VK_LSHIFT => Avalonia.Input.Key.LeftShift,
                VIRTUAL_KEY.VK_RSHIFT => Avalonia.Input.Key.RightShift,
                VIRTUAL_KEY.VK_LCONTROL => Avalonia.Input.Key.LeftCtrl,
                VIRTUAL_KEY.VK_RCONTROL => Avalonia.Input.Key.RightCtrl,
                VIRTUAL_KEY.VK_LMENU => Avalonia.Input.Key.LeftAlt,
                VIRTUAL_KEY.VK_RMENU => Avalonia.Input.Key.RightAlt,
                VIRTUAL_KEY.VK_NUMLOCK => Avalonia.Input.Key.NumLock,
                VIRTUAL_KEY.VK_SCROLL => Avalonia.Input.Key.Scroll,

                // Browser keys
                VIRTUAL_KEY.VK_BROWSER_BACK => Avalonia.Input.Key.BrowserBack,
                VIRTUAL_KEY.VK_BROWSER_FORWARD => Avalonia.Input.Key.BrowserForward,
                VIRTUAL_KEY.VK_BROWSER_REFRESH => Avalonia.Input.Key.BrowserRefresh,
                VIRTUAL_KEY.VK_BROWSER_STOP => Avalonia.Input.Key.BrowserStop,
                VIRTUAL_KEY.VK_BROWSER_SEARCH => Avalonia.Input.Key.BrowserSearch,
                VIRTUAL_KEY.VK_BROWSER_FAVORITES => Avalonia.Input.Key.BrowserFavorites,
                VIRTUAL_KEY.VK_BROWSER_HOME => Avalonia.Input.Key.BrowserHome,

                // Media keys
                VIRTUAL_KEY.VK_VOLUME_MUTE => Avalonia.Input.Key.VolumeMute,
                VIRTUAL_KEY.VK_VOLUME_DOWN => Avalonia.Input.Key.VolumeDown,
                VIRTUAL_KEY.VK_VOLUME_UP => Avalonia.Input.Key.VolumeUp,
                VIRTUAL_KEY.VK_MEDIA_NEXT_TRACK => Avalonia.Input.Key.MediaNextTrack,
                VIRTUAL_KEY.VK_MEDIA_PREV_TRACK => Avalonia.Input.Key.MediaPreviousTrack,
                VIRTUAL_KEY.VK_MEDIA_STOP => Avalonia.Input.Key.MediaStop,
                VIRTUAL_KEY.VK_MEDIA_PLAY_PAUSE => Avalonia.Input.Key.MediaPlayPause,
                VIRTUAL_KEY.VK_LAUNCH_MAIL => Avalonia.Input.Key.LaunchMail,
                VIRTUAL_KEY.VK_LAUNCH_MEDIA_SELECT => Avalonia.Input.Key.SelectMedia,
                VIRTUAL_KEY.VK_LAUNCH_APP1 => Avalonia.Input.Key.LaunchApplication1,
                VIRTUAL_KEY.VK_LAUNCH_APP2 => Avalonia.Input.Key.LaunchApplication2,

                // OEM keys
                VIRTUAL_KEY.VK_OEM_1 => Avalonia.Input.Key.OemSemicolon,
                VIRTUAL_KEY.VK_OEM_PLUS => Avalonia.Input.Key.OemPlus,
                VIRTUAL_KEY.VK_OEM_COMMA => Avalonia.Input.Key.OemComma,
                VIRTUAL_KEY.VK_OEM_MINUS => Avalonia.Input.Key.OemMinus,
                VIRTUAL_KEY.VK_OEM_PERIOD => Avalonia.Input.Key.OemPeriod,
                VIRTUAL_KEY.VK_OEM_2 => Avalonia.Input.Key.OemQuestion,
                VIRTUAL_KEY.VK_OEM_3 => Avalonia.Input.Key.OemTilde,
                VIRTUAL_KEY.VK_ABNT_C1 => Avalonia.Input.Key.AbntC1,
                VIRTUAL_KEY.VK_ABNT_C2 => Avalonia.Input.Key.AbntC2,
                VIRTUAL_KEY.VK_OEM_4 => Avalonia.Input.Key.OemOpenBrackets,
                VIRTUAL_KEY.VK_OEM_5 => Avalonia.Input.Key.OemPipe,
                VIRTUAL_KEY.VK_OEM_6 => Avalonia.Input.Key.OemCloseBrackets,
                VIRTUAL_KEY.VK_OEM_7 => Avalonia.Input.Key.OemQuotes,
                VIRTUAL_KEY.VK_OEM_8 => Avalonia.Input.Key.Oem8,
                VIRTUAL_KEY.VK_OEM_102 => Avalonia.Input.Key.OemBackslash,
                VIRTUAL_KEY.VK_OEM_CLEAR => Avalonia.Input.Key.OemClear,

                // Other special keys
                VIRTUAL_KEY.VK_CLEAR => Avalonia.Input.Key.Clear,
                VIRTUAL_KEY.VK_CANCEL => Avalonia.Input.Key.Cancel,
                VIRTUAL_KEY.VK_PRINT => Avalonia.Input.Key.Print,
                VIRTUAL_KEY.VK_EXECUTE => Avalonia.Input.Key.Execute,
                VIRTUAL_KEY.VK_SELECT => Avalonia.Input.Key.Select,
                VIRTUAL_KEY.VK_ATTN => Avalonia.Input.Key.Attn,
                VIRTUAL_KEY.VK_CRSEL => Avalonia.Input.Key.CrSel,
                VIRTUAL_KEY.VK_EXSEL => Avalonia.Input.Key.ExSel,
                VIRTUAL_KEY.VK_EREOF => Avalonia.Input.Key.EraseEof,
                VIRTUAL_KEY.VK_PLAY => Avalonia.Input.Key.Play,
                VIRTUAL_KEY.VK_ZOOM => Avalonia.Input.Key.Zoom,
                VIRTUAL_KEY.VK_NONAME => Avalonia.Input.Key.NoName,
                VIRTUAL_KEY.VK_PA1 => Avalonia.Input.Key.Pa1,

                // IME keys
                VIRTUAL_KEY.VK_KANA => Avalonia.Input.Key.KanaMode,
                VIRTUAL_KEY.VK_JUNJA => Avalonia.Input.Key.JunjaMode,
                VIRTUAL_KEY.VK_FINAL => Avalonia.Input.Key.FinalMode,
                VIRTUAL_KEY.VK_HANJA => Avalonia.Input.Key.HanjaMode,
                VIRTUAL_KEY.VK_CONVERT => Avalonia.Input.Key.ImeConvert,
                VIRTUAL_KEY.VK_NONCONVERT => Avalonia.Input.Key.ImeNonConvert,
                VIRTUAL_KEY.VK_ACCEPT => Avalonia.Input.Key.ImeAccept,
                VIRTUAL_KEY.VK_MODECHANGE => Avalonia.Input.Key.ImeModeChange,
                VIRTUAL_KEY.VK_PROCESSKEY => Avalonia.Input.Key.ImeProcessed,

                // DBCS keys
                VIRTUAL_KEY.VK_DBE_ALPHANUMERIC => Avalonia.Input.Key.DbeAlphanumeric,
                VIRTUAL_KEY.VK_DBE_KATAKANA => Avalonia.Input.Key.DbeKatakana,
                VIRTUAL_KEY.VK_DBE_HIRAGANA => Avalonia.Input.Key.DbeHiragana,
                VIRTUAL_KEY.VK_DBE_SBCSCHAR => Avalonia.Input.Key.DbeSbcsChar,
                VIRTUAL_KEY.VK_DBE_DBCSCHAR => Avalonia.Input.Key.DbeDbcsChar,
                VIRTUAL_KEY.VK_DBE_ROMAN => Avalonia.Input.Key.DbeRoman,

                _ => 0
            };
        }

        // ReSharper disable InconsistentNaming
        // ReSharper disable IdentifierTypo
        // ReSharper disable UnusedMember.Local
        private enum VIRTUAL_KEY : ushort
        {
            VK_0 = 48,
            VK_1 = 49,
            VK_2 = 50,
            VK_3 = 51,
            VK_4 = 52,
            VK_5 = 53,
            VK_6 = 54,
            VK_7 = 55,
            VK_8 = 56,
            VK_9 = 57,
            VK_A = 65,
            VK_B = 66,
            VK_C = 67,
            VK_D = 68,
            VK_E = 69,
            VK_F = 70,
            VK_G = 71,
            VK_H = 72,
            VK_I = 73,
            VK_J = 74,
            VK_K = 75,
            VK_L = 76,
            VK_M = 77,
            VK_N = 78,
            VK_O = 79,
            VK_P = 80,
            VK_Q = 81,
            VK_R = 82,
            VK_S = 83,
            VK_T = 84,
            VK_U = 85,
            VK_V = 86,
            VK_W = 87,
            VK_X = 88,
            VK_Y = 89,
            VK_Z = 90,
            VK_ABNT_C1 = 193,
            VK_ABNT_C2 = 194,
            VK_DBE_ALPHANUMERIC = 240,
            VK_DBE_CODEINPUT = 250,
            VK_DBE_DBCSCHAR = 244,
            VK_DBE_DETERMINESTRING = 252,
            VK_DBE_ENTERDLGCONVERSIONMODE = 253,
            VK_DBE_ENTERIMECONFIGMODE = 248,
            VK_DBE_ENTERWORDREGISTERMODE = 247,
            VK_DBE_FLUSHSTRING = 249,
            VK_DBE_HIRAGANA = 242,
            VK_DBE_KATAKANA = 241,
            VK_DBE_NOCODEINPUT = 251,
            VK_DBE_NOROMAN = 246,
            VK_DBE_ROMAN = 245,
            VK_DBE_SBCSCHAR = 243,
            VK__none_ = 255,
            VK_LBUTTON = 1,
            VK_RBUTTON = 2,
            VK_CANCEL = 3,
            VK_MBUTTON = 4,
            VK_XBUTTON1 = 5,
            VK_XBUTTON2 = 6,
            VK_BACK = 8,
            VK_TAB = 9,
            VK_CLEAR = 12,
            VK_RETURN = 13,
            VK_SHIFT = 16,
            VK_CONTROL = 17,
            VK_MENU = 18,
            VK_PAUSE = 19,
            VK_CAPITAL = 20,
            VK_KANA = 21,
            VK_HANGEUL = 21,
            VK_HANGUL = 21,
            VK_IME_ON = 22,
            VK_JUNJA = 23,
            VK_FINAL = 24,
            VK_HANJA = 25,
            VK_KANJI = 25,
            VK_IME_OFF = 26,
            VK_ESCAPE = 27,
            VK_CONVERT = 28,
            VK_NONCONVERT = 29,
            VK_ACCEPT = 30,
            VK_MODECHANGE = 31,
            VK_SPACE = 32,
            VK_PRIOR = 33,
            VK_NEXT = 34,
            VK_END = 35,
            VK_HOME = 36,
            VK_LEFT = 37,
            VK_UP = 38,
            VK_RIGHT = 39,
            VK_DOWN = 40,
            VK_SELECT = 41,
            VK_PRINT = 42,
            VK_EXECUTE = 43,
            VK_SNAPSHOT = 44,
            VK_INSERT = 45,
            VK_DELETE = 46,
            VK_HELP = 47,
            VK_LWIN = 91,
            VK_RWIN = 92,
            VK_APPS = 93,
            VK_SLEEP = 95,
            VK_NUMPAD0 = 96,
            VK_NUMPAD1 = 97,
            VK_NUMPAD2 = 98,
            VK_NUMPAD3 = 99,
            VK_NUMPAD4 = 100,
            VK_NUMPAD5 = 101,
            VK_NUMPAD6 = 102,
            VK_NUMPAD7 = 103,
            VK_NUMPAD8 = 104,
            VK_NUMPAD9 = 105,
            VK_MULTIPLY = 106,
            VK_ADD = 107,
            VK_SEPARATOR = 108,
            VK_SUBTRACT = 109,
            VK_DECIMAL = 110,
            VK_DIVIDE = 111,
            VK_F1 = 112,
            VK_F2 = 113,
            VK_F3 = 114,
            VK_F4 = 115,
            VK_F5 = 116,
            VK_F6 = 117,
            VK_F7 = 118,
            VK_F8 = 119,
            VK_F9 = 120,
            VK_F10 = 121,
            VK_F11 = 122,
            VK_F12 = 123,
            VK_F13 = 124,
            VK_F14 = 125,
            VK_F15 = 126,
            VK_F16 = 127,
            VK_F17 = 128,
            VK_F18 = 129,
            VK_F19 = 130,
            VK_F20 = 131,
            VK_F21 = 132,
            VK_F22 = 133,
            VK_F23 = 134,
            VK_F24 = 135,
            VK_NAVIGATION_VIEW = 136,
            VK_NAVIGATION_MENU = 137,
            VK_NAVIGATION_UP = 138,
            VK_NAVIGATION_DOWN = 139,
            VK_NAVIGATION_LEFT = 140,
            VK_NAVIGATION_RIGHT = 141,
            VK_NAVIGATION_ACCEPT = 142,
            VK_NAVIGATION_CANCEL = 143,
            VK_NUMLOCK = 144,
            VK_SCROLL = 145,
            VK_OEM_NEC_EQUAL = 146,
            VK_OEM_FJ_JISHO = 146,
            VK_OEM_FJ_MASSHOU = 147,
            VK_OEM_FJ_TOUROKU = 148,
            VK_OEM_FJ_LOYA = 149,
            VK_OEM_FJ_ROYA = 150,
            VK_LSHIFT = 160,
            VK_RSHIFT = 161,
            VK_LCONTROL = 162,
            VK_RCONTROL = 163,
            VK_LMENU = 164,
            VK_RMENU = 165,
            VK_BROWSER_BACK = 166,
            VK_BROWSER_FORWARD = 167,
            VK_BROWSER_REFRESH = 168,
            VK_BROWSER_STOP = 169,
            VK_BROWSER_SEARCH = 170,
            VK_BROWSER_FAVORITES = 171,
            VK_BROWSER_HOME = 172,
            VK_VOLUME_MUTE = 173,
            VK_VOLUME_DOWN = 174,
            VK_VOLUME_UP = 175,
            VK_MEDIA_NEXT_TRACK = 176,
            VK_MEDIA_PREV_TRACK = 177,
            VK_MEDIA_STOP = 178,
            VK_MEDIA_PLAY_PAUSE = 179,
            VK_LAUNCH_MAIL = 180,
            VK_LAUNCH_MEDIA_SELECT = 181,
            VK_LAUNCH_APP1 = 182,
            VK_LAUNCH_APP2 = 183,
            VK_OEM_1 = 186,
            VK_OEM_PLUS = 187,
            VK_OEM_COMMA = 188,
            VK_OEM_MINUS = 189,
            VK_OEM_PERIOD = 190,
            VK_OEM_2 = 191,
            VK_OEM_3 = 192,
            VK_GAMEPAD_A = 195,
            VK_GAMEPAD_B = 196,
            VK_GAMEPAD_X = 197,
            VK_GAMEPAD_Y = 198,
            VK_GAMEPAD_RIGHT_SHOULDER = 199,
            VK_GAMEPAD_LEFT_SHOULDER = 200,
            VK_GAMEPAD_LEFT_TRIGGER = 201,
            VK_GAMEPAD_RIGHT_TRIGGER = 202,
            VK_GAMEPAD_DPAD_UP = 203,
            VK_GAMEPAD_DPAD_DOWN = 204,
            VK_GAMEPAD_DPAD_LEFT = 205,
            VK_GAMEPAD_DPAD_RIGHT = 206,
            VK_GAMEPAD_MENU = 207,
            VK_GAMEPAD_VIEW = 208,
            VK_GAMEPAD_LEFT_THUMBSTICK_BUTTON = 209,
            VK_GAMEPAD_RIGHT_THUMBSTICK_BUTTON = 210,
            VK_GAMEPAD_LEFT_THUMBSTICK_UP = 211,
            VK_GAMEPAD_LEFT_THUMBSTICK_DOWN = 212,
            VK_GAMEPAD_LEFT_THUMBSTICK_RIGHT = 213,
            VK_GAMEPAD_LEFT_THUMBSTICK_LEFT = 214,
            VK_GAMEPAD_RIGHT_THUMBSTICK_UP = 215,
            VK_GAMEPAD_RIGHT_THUMBSTICK_DOWN = 216,
            VK_GAMEPAD_RIGHT_THUMBSTICK_RIGHT = 217,
            VK_GAMEPAD_RIGHT_THUMBSTICK_LEFT = 218,
            VK_OEM_4 = 219,
            VK_OEM_5 = 220,
            VK_OEM_6 = 221,
            VK_OEM_7 = 222,
            VK_OEM_8 = 223,
            VK_OEM_AX = 225,
            VK_OEM_102 = 226,
            VK_ICO_HELP = 227,
            VK_ICO_00 = 228,
            VK_PROCESSKEY = 229,
            VK_ICO_CLEAR = 230,
            VK_PACKET = 231,
            VK_OEM_RESET = 233,
            VK_OEM_JUMP = 234,
            VK_OEM_PA1 = 235,
            VK_OEM_PA2 = 236,
            VK_OEM_PA3 = 237,
            VK_OEM_WSCTRL = 238,
            VK_OEM_CUSEL = 239,
            VK_OEM_ATTN = 240,
            VK_OEM_FINISH = 241,
            VK_OEM_COPY = 242,
            VK_OEM_AUTO = 243,
            VK_OEM_ENLW = 244,
            VK_OEM_BACKTAB = 245,
            VK_ATTN = 246,
            VK_CRSEL = 247,
            VK_EXSEL = 248,
            VK_EREOF = 249,
            VK_PLAY = 250,
            VK_ZOOM = 251,
            VK_NONAME = 252,
            VK_PA1 = 253,
            VK_OEM_CLEAR = 254,
        }
        // ReSharper restore InconsistentNaming
        // ReSharper restore IdentifierTypo
        // ReSharper restore UnusedMember.Local
    }
}