using System;
using System.Globalization;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace SMT.PriceGun200;

public sealed partial class Plugin
{
    private bool _menuVisible;
    private bool _menuResumeBlocked;
    private Rect _menuRect;
    private Vector2 _menuScroll;
    private CursorLockMode _previousCursorLock;
    private bool _previousCursorVisible;
    private object? _menuOptions;
    private bool _previousOptions;
    private int _captureKey;
    private MenuDraft? _draft;
    private string _menuMessage = "";
    private GUIStyle? _opaqueWindow;
    private Texture2D? _opaqueBackground;

    private sealed class MenuDraft
    {
        internal bool Enabled, Scanner, RequireScanner, Checkout, Toggle, Debug, FixColliders;
        internal string Multiplier = "", Distance = "", Interval = "", PickupDelay = "", ConfirmDelay = "";
        internal KeyboardShortcut ScannerKey;
        internal KeyboardShortcut CheckoutKey;
    }

    private bool UpdateMenuInput()
    {
        if (Input.GetKeyDown(KeyCode.F12))
        {
            if (_menuVisible) CloseMenu(); else OpenMenu();
            return true;
        }
        if (!_menuVisible)
        {
            if (!_menuResumeBlocked)
                return false;
            if (_hotkey.Value.IsPressed() || _checkoutHotkey.Value.IsPressed())
                return true;
            _menuResumeBlocked = false;
            return false;
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_captureKey != 0) _captureKey = 0; else CloseMenu();
            return true;
        }
        if (_captureKey != 0 && Input.anyKeyDown && _draft != null)
        {
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                if (key == KeyCode.None || key == KeyCode.F12 || !Input.GetKeyDown(key))
                    continue;
                if (_captureKey == 1) _draft.ScannerKey = new KeyboardShortcut(key);
                else _draft.CheckoutKey = new KeyboardShortcut(key);
                _captureKey = 0;
                break;
            }
        }
        KeepMenuInputBlocked();
        return true;
    }

    private void OpenMenu()
    {
        _checkoutHold?.Stop();
        _checkout?.StopHeldPayment();
        _previousCursorLock = Cursor.lockState;
        _previousCursorVisible = Cursor.visible;
        _menuVisible = true;
        _menuResumeBlocked = true;
        _captureKey = 0;
        _menuMessage = "Změny se použijí až tlačítkem Použít a uložit.";
        ReadMenuDraft();
        _menuRect = new Rect(Mathf.Max(0f, (Screen.width - 570f) / 2f), 35f,
            Mathf.Min(570f, Screen.width), Mathf.Min(720f, Screen.height - 60f));
        KeepMenuInputBlocked();
    }

    private void CloseMenu()
    {
        if (!_menuVisible)
            return;
        _menuVisible = false;
        _captureKey = 0;
        if (_menuOptions != null)
            SetOptionsValue(_menuOptions, _previousOptions);
        _menuOptions = null;
        Cursor.lockState = _previousCursorLock;
        Cursor.visible = _previousCursorVisible;
        _menuResumeBlocked = true;
    }

    private void KeepMenuInputBlocked()
    {
        var variable = GameContext.OptionsVariable;
        if (!ReferenceEquals(variable, _menuOptions))
        {
            if (_menuOptions != null) SetOptionsValue(_menuOptions, _previousOptions);
            _menuOptions = variable;
            _previousOptions = variable != null && GameBridge.TryGetMemberValue(variable, "Value", out var previous) && previous is bool value && value;
        }
        if (_menuOptions != null) SetOptionsValue(_menuOptions, true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private static void SetOptionsValue(object variable, bool value)
    {
        variable.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)?.SetValue(variable, value);
    }

    private void OnGUI()
    {
        if (!_menuVisible || _draft == null)
            return;
        GUI.depth = -1000;
        if (_opaqueWindow == null || _opaqueBackground == null)
        {
            _opaqueBackground = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            _opaqueBackground.SetPixel(0, 0, new Color(0.09f, 0.10f, 0.12f, 1f));
            _opaqueBackground.Apply();
            _opaqueWindow = new GUIStyle(GUI.skin.window);
            foreach (var state in new[] { _opaqueWindow.normal, _opaqueWindow.hover, _opaqueWindow.active, _opaqueWindow.focused,
                _opaqueWindow.onNormal, _opaqueWindow.onHover, _opaqueWindow.onActive, _opaqueWindow.onFocused })
            {
                state.background = _opaqueBackground;
                state.textColor = Color.white;
            }
        }
        var previousColor = GUI.color;
        var previousBackground = GUI.backgroundColor;
        try
        {
            GUI.color = Color.white;
            GUI.backgroundColor = Color.white;
            _menuRect = GUILayout.Window(0x534D5407, _menuRect, DrawMenu, PluginName + " " + PluginVersion, _opaqueWindow);
        }
        finally
        {
            GUI.color = previousColor;
            GUI.backgroundColor = previousBackground;
        }
    }

    private void DrawMenu(int windowId)
    {
        if (_draft == null)
            return;
        GUILayout.Label("F12 / Esc: zavřít. Po dobu menu se automatika zastaví.");
        _menuScroll = GUILayout.BeginScrollView(_menuScroll, GUILayout.Height(Mathf.Max(160f, _menuRect.height - 145f)));
        _draft.Enabled = GUILayout.Toggle(_draft.Enabled, "Zapnout celý mod");
        GUILayout.Space(8);
        GUILayout.Label("SCANNER – cenovky regálů");
        _draft.Scanner = GUILayout.Toggle(_draft.Scanner, "Zapnout změnu cen");
        _draft.RequireScanner = GUILayout.Toggle(_draft.RequireScanner, "Vyžadovat držený scanner");
        NumberRow("Násobek market price (2.01 = 201 %)", ref _draft.Multiplier);
        NumberRow("Dosah scanneru (1–20 m)", ref _draft.Distance);
        KeyRow("Tlačítko scanneru", _draft.ScannerKey, 1);
        GUILayout.Space(8);
        GUILayout.Label("POKLADNA – skenování a platby");
        _draft.Checkout = GUILayout.Toggle(_draft.Checkout, "Zapnout pomoc na pokladně");
        _draft.FixColliders = GUILayout.Toggle(_draft.FixColliders, "Opravit vadné collidery zrcadlených pokladen");
        _draft.Toggle = GUILayout.Toggle(_draft.Toggle, "Kliknutím zapnout/vypnout automatické skenování a platby");
        KeyRow("Tlačítko pokladny", _draft.CheckoutKey, 2);
        NumberRow("Interval skenování (0.05–2 s)", ref _draft.Interval);
        NumberRow("Po převzetí karty/hotovosti (0–60 s)", ref _draft.PickupDelay);
        NumberRow("Před potvrzením platby/vrácení (0–60 s)", ref _draft.ConfirmDelay);
        GUILayout.Space(8);
        _draft.Debug = GUILayout.Toggle(_draft.Debug, "Podrobný debug log");
        GUILayout.Label("Mouse0 = levé, Mouse1 = pravé, Mouse2 = prostřední.\nNa platebním prvku má kliknutí přednost; na pokladně/pásu stejné tlačítko přepíná automatiku. Esc zruší změnu tlačítka. F12 je vyhrazené menu.");
        GUILayout.EndScrollView();
        GUILayout.Label(_menuMessage);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Použít a uložit")) ApplyMenuDraft();
        if (GUILayout.Button("Reload configu")) ReloadMenuConfig();
        if (GUILayout.Button("Zavřít")) CloseMenu();
        GUILayout.EndHorizontal();
        GUI.DragWindow(new Rect(0f, 0f, _menuRect.width, 22f));
    }

    private static void NumberRow(string label, ref string text)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.MinWidth(300f));
        text = GUILayout.TextField(text, GUILayout.Width(100f));
        GUILayout.EndHorizontal();
    }

    private void KeyRow(string label, KeyboardShortcut shortcut, int target)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.MinWidth(300f));
        if (GUILayout.Button(_captureKey == target ? "Stiskni tlačítko..." : shortcut.ToString(), GUILayout.Width(160f)))
            _captureKey = target;
        GUILayout.EndHorizontal();
    }

    private static string NumberText(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private void ReadMenuDraft()
    {
        _captureKey = 0;
        _draft = new MenuDraft {
            Enabled = _enabled.Value, Scanner = _scannerEnabled.Value, RequireScanner = _requirePricingTool.Value,
            Checkout = _checkoutEnabled.Value, Toggle = _toggleAutomation.Value, Debug = _debugLogging.Value, FixColliders = _fixCheckoutColliders.Value,
            Multiplier = NumberText(_multiplier.Value), Distance = NumberText(_maxDistance.Value), Interval = NumberText(_scanInterval.Value),
            PickupDelay = NumberText(_afterPickupDelay.Value), ConfirmDelay = NumberText(_confirmDelay.Value),
            ScannerKey = _hotkey.Value, CheckoutKey = _checkoutHotkey.Value
        };
    }

    private void ApplyMenuDraft()
    {
        if (_draft == null)
            return;
        if (!MenuSettings.TryNumber(_draft.Multiplier, 0f, 100f, out var multiplier) ||
            !MenuSettings.TryNumber(_draft.Distance, 1f, 20f, out var distance) ||
            !MenuSettings.TryNumber(_draft.Interval, 0.05f, 2f, out var interval) ||
            !MenuSettings.TryNumber(_draft.PickupDelay, 0f, 60f, out var pickup) ||
            !MenuSettings.TryNumber(_draft.ConfirmDelay, 0f, 60f, out var confirm))
        {
            _menuMessage = "Neplatná hodnota nebo rozsah. Použij čísla, tečku nebo čárku.";
            return;
        }
        var autoSave = Config.SaveOnConfigSet;
        Config.SaveOnConfigSet = false;
        try
        {
            _enabled.Value = _draft.Enabled;
            _scannerEnabled.Value = _draft.Scanner;
            _requirePricingTool.Value = _draft.RequireScanner;
            _checkoutEnabled.Value = _draft.Checkout;
            _fixCheckoutColliders.Value = _draft.FixColliders;
            _toggleAutomation.Value = _draft.Toggle;
            _debugLogging.Value = _draft.Debug;
            _multiplier.Value = multiplier;
            _maxDistance.Value = distance;
            _scanInterval.Value = interval;
            _afterPickupDelay.Value = pickup;
            _confirmDelay.Value = confirm;
            _hotkey.Value = _draft.ScannerKey;
            _checkoutHotkey.Value = _draft.CheckoutKey;
            Config.Save();
            _menuMessage = "Nastavení použito a uloženo. Restart hry není potřeba.";
        }
        catch (Exception ex)
        {
            _menuMessage = "Nastavení v paměti změněno, uložení selhalo. Zkus uložit znovu.";
            Logger.LogError($"Saving menu settings failed: {ex}");
        }
        finally { Config.SaveOnConfigSet = autoSave; }
    }

    private void ReloadMenuConfig()
    {
        _checkoutHold?.Stop();
        _checkout?.StopHeldPayment();
        var autoSave = Config.SaveOnConfigSet;
        Config.SaveOnConfigSet = false;
        try
        {
            Config.Reload();
            _scanInterval.Value = Mathf.Clamp(_scanInterval.Value, 0.05f, 2f);
            _maxDistance.Value = Mathf.Clamp(_maxDistance.Value, 1f, 20f);
            _multiplier.Value = float.IsNaN(_multiplier.Value) || float.IsInfinity(_multiplier.Value) ? 2.01f : Mathf.Clamp(_multiplier.Value, 0f, 100f);
            _afterPickupDelay.Value = CheckoutDelay.Normalize(_afterPickupDelay.Value);
            _confirmDelay.Value = CheckoutDelay.Normalize(_confirmDelay.Value);
            ReadMenuDraft();
            _menuMessage = "Config načten z disku a použit. Rozepsané změny menu byly nahrazeny.";
        }
        catch (Exception ex)
        {
            _menuMessage = "Reload configu selhal; podrobnosti jsou v logu.";
            Logger.LogError($"Reloading config failed: {ex}");
        }
        finally { Config.SaveOnConfigSet = autoSave; }
    }

    private void OnDisable()
    {
        _colliderRepair?.Dispose();
        _checkoutHold?.Stop();
        _checkout?.StopHeldPayment();
        CloseMenu();
    }

    private void OnDestroy()
    {
        _colliderRepair?.Dispose();
        _checkoutHold?.Stop();
        _checkout?.StopHeldPayment();
        CloseMenu();
        if (_opaqueBackground != null) Destroy(_opaqueBackground);
        _opaqueBackground = null;
        _opaqueWindow = null;
    }
}
