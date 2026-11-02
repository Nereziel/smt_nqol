using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace SMT.PriceGun200;

internal sealed class CheckoutBridge
{
    private readonly ManualLogSource _log;
    private readonly Func<bool> _verbose;
    private readonly CheckoutConfirmationGuard _guard = new();
    private Component? _pendingCheckout;
    private int _pendingCustomer;
    private Component? _preparedConfirm;
    private Component? _preparedAmount;
    private string? _preparedText;
    private float _preparedTotal;
    private float _preparedChange;
    private bool _preparedCash;
    private int _preparedFrame;
    private float _nextPendingCheck;
    private bool _awaitingPanel;
    private bool _activationCash;
    private float _activationAt;
    private readonly Func<float> _afterPickupSeconds;
    private readonly Func<float> _confirmSeconds;
    private readonly CheckoutDelay _afterPickupDelay = new();
    private readonly CheckoutDelay _confirmDelay = new();
    private bool _waitingFill;
    private bool _fillCash;

    internal CheckoutBridge(ManualLogSource log, Func<bool> verbose, Func<float> afterPickupSeconds, Func<float> confirmSeconds)
    {
        _log = log;
        _verbose = verbose;
        _afterPickupSeconds = afterPickupSeconds;
        _confirmSeconds = confirmSeconds;
    }

    internal void RefreshPending()
    {
        if (_pendingCheckout == null)
        {
            CancelPending();
            return;
        }
        if (Time.unscaledTime < _nextPendingCheck)
            return;
        _nextPendingCheck = Time.unscaledTime + 0.05f;
        var checkout = _pendingCheckout;
        var customer = ReadCustomer(checkout);
        if (customer == null || customer.GetInstanceID() != _pendingCustomer)
        {
            CancelPending();
            return;
        }
        var cashActive = IsActive(checkout.transform, "CashRegisterCanvas/Container");
        var cardActive = IsActive(checkout.transform, "CreditCardCanvas/Container");
        if (_awaitingPanel)
        {
            // Acceptance is a network command: the panel appears after the server's RPC.
            if (Time.unscaledTime - _activationAt > 3f || IsInOptions() || !HasCashierPermission() ||
                !ReadFloat(checkout, "checkoutProductValue", out var total) ||
                Math.Abs((double)total - _preparedTotal) > 0.005d)
            {
                Debug("Checkout acceptance cancelled: payment panel timed out or transaction changed.");
                CancelPending();
                return;
            }
            if (cashActive && cardActive || (_activationCash ? cardActive : cashActive))
            {
                Debug("Checkout acceptance cancelled: unexpected payment mode.");
                CancelPending();
                return;
            }
            if (_activationCash ? cashActive : cardActive)
            {
                _awaitingPanel = false;
                QueuePreparation(checkout, customer, total, _activationCash);
            }
            return;
        }
        _guard.Observe(checkout.GetInstanceID(), customer.GetInstanceID(), cashActive || cardActive);
        if (!cashActive && !cardActive)
        {
            CancelPending();
            return;
        }
        if (_waitingFill)
        {
            if (IsInOptions() || !HasCashierPermission() || cashActive == cardActive || cashActive != _fillCash ||
                !ReadFloat(checkout, "checkoutProductValue", out var total) ||
                Math.Abs((double)total - _preparedTotal) > 0.005d)
            {
                CancelPending();
                return;
            }
            if (!_afterPickupDelay.IsReady(Time.unscaledTime))
                return;
            _waitingFill = false;
            if (!TryPreparePayment(checkout, customer, total, _fillCash))
                CancelPending();
            return;
        }
        CompletePreparedPayment();
    }

    private void QueuePreparation(Component checkout, GameObject customer, float total, bool cash)
    {
        _pendingCheckout = checkout;
        _pendingCustomer = customer.GetInstanceID();
        _preparedTotal = total;
        _preparedConfirm = null;
        _fillCash = cash;
        _waitingFill = true;
        _guard.MarkPending(checkout.GetInstanceID(), _pendingCustomer);
        _afterPickupDelay.Start(Time.unscaledTime, _afterPickupSeconds());
        Debug("Checkout payment panel ready; waiting for AfterPickupDelay before filling the amount.");
    }

    private void CancelPending()
    {
        _guard.Observe(0, 0, false);
        _pendingCheckout = null;
        _preparedConfirm = null;
        _preparedAmount = null;
        _preparedText = null;
        _awaitingPanel = false;
        _waitingFill = false;
        _nextPendingCheck = 0f;
        _afterPickupDelay.Clear();
        _confirmDelay.Clear();
    }

    private void CompletePreparedPayment()
    {
        if (_preparedConfirm == null || _pendingCheckout == null || Time.frameCount <= _preparedFrame)
            return;
        var checkout = _pendingCheckout;
        if (_confirmDelay.HasTimedOut(Time.unscaledTime, 1f) || IsInOptions() ||
            !HasCashierPermission() || !ReadFloat(checkout, "checkoutProductValue", out var total) ||
            Math.Abs((double)total - _preparedTotal) > 0.005d)
        {
            Debug("Prepared checkout confirmation cancelled: context changed or validation timed out.");
            _guard.Observe(0, 0, false);
            _preparedConfirm = null;
            _pendingCheckout = null;
            return;
        }
        if (!IsActive(checkout.transform, _preparedCash ? "CashRegisterCanvas/Container" : "CreditCardCanvas/Container"))
            return;
        if (_preparedCash)
        {
            if (!ReadBool(checkout, "allowMoneyReturn") ||
                !ReadFloat(checkout, "currentAmountToReturn", out var actual) ||
                !ReadFloat(checkout, "moneyToReturn", out var expected) ||
                Math.Abs((double)actual - expected) > 0.005d ||
                Math.Abs((double)expected - _preparedChange) > 0.005d)
                return;
        }
        else
        {
            if (_preparedAmount == null ||
                !TryVariable(_preparedAmount, "GetFsmString", "BaseString", out var baseString) ||
                !GameBridge.TryGetMemberValue(baseString, "Value", out var text) || text is not string value || value != _preparedText ||
                !TryVariable(_preparedAmount, "GetFsmBool", "AmountMatches", out var matches) || !ReadBool(matches, "Value"))
                return;
        }
        if (_preparedConfirm is not MonoBehaviour behaviour || !behaviour.isActiveAndEnabled)
            return;
        if (!_confirmDelay.IsReady(Time.unscaledTime))
            return;
        // Give PlayMaker a frame to run its normal initialization and amount validation.
        var confirm = _preparedConfirm;
        _preparedConfirm = null;
        try
        {
            Invoke(confirm, "SendEvent", new[] { typeof(string) }, "Send_Data");
        }
        catch
        {
            _pendingCheckout = null;
            _guard.Observe(0, 0, false);
            throw;
        }
        _log.LogInfo($"Checkout {(_preparedCash ? "cash change" : "card amount")} confirmation sent: total {_preparedTotal:0.00}.");
    }

    private Component? GetAimedCheckout(out CheckoutControl control, bool requireControl = true)
    {
        control = CheckoutControl.None;
        var camera = Camera.main;
        if (camera == null)
        {
            Debug("Checkout ignored: main camera not found.");
            return null;
        }
        var ray = Cursor.visible && Cursor.lockState != CursorLockMode.Locked
            ? camera.ScreenPointToRay(Input.mousePosition)
            : camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out var hit, 4f, ~0, QueryTriggerInteraction.Collide))
        {
            Debug("Checkout ignored: no control under the aim/cursor within 4 metres.");
            return null;
        }
        var target = hit.collider.transform;
        var checkout = target.GetComponentsInParent<Component>(true).FirstOrDefault(c =>
            c != null && c.GetType().Name == "Data_Container");
        if (checkout == null && !requireControl)
        {
            var product = target.GetComponentsInParent<Component>(true).FirstOrDefault(c =>
                c != null && c.GetType().Name == "ProductCheckoutSpawn");
            if (product != null && GameBridge.TryGetMemberValue(product, "NetworkcheckoutOBJ", out var owner) && owner is GameObject register && register != null)
                checkout = register.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "Data_Container");
        }
        if (checkout == null)
        {
            Debug($"Checkout ignored: aimed object {target.name} is not part of a checkout.");
            return null;
        }
        var parts = new System.Collections.Generic.List<string>();
        for (var current = target; current != null && current != checkout.transform; current = current.parent)
            parts.Add(current.name);
        parts.Reverse();
        var path = string.Join("/", parts);
        control = CheckoutControls.Classify(path);
        Debug($"Checkout hit: {checkout.name}/{path}; control={control}.");
        return !requireControl || control != CheckoutControl.None ? checkout : null;
    }

    internal bool TryConfirmPayment()
    {
        var checkout = GetAimedCheckout(out var control);
        return checkout != null && TryConfirmPayment(checkout, control);
    }

    internal Component? FindHoldCheckout() => GetAimedCheckout(out _, false);

    internal static bool CanOperate() => !IsInOptions() && HasCashierPermission();

    internal void StopHeldPayment()
    {
        // Commands already sent cannot be recalled; discard only the remaining local steps.
        if (_awaitingPanel || _waitingFill || _preparedConfirm != null)
            CancelPending();
    }

    internal bool TryConfirmHeldPayment(Component checkout)
    {
        CheckoutControl control;
        if (IsActive(checkout.transform, "CashRegisterCanvas/Container")) control = CheckoutControl.CashControls;
        else if (IsActive(checkout.transform, "CreditCardCanvas/Container")) control = CheckoutControl.CardControls;
        else if (IsActive(checkout.transform, "Payments/Payment_Money")) control = CheckoutControl.OfferedCash;
        else if (IsActive(checkout.transform, "Payments/Payment_Card")) control = CheckoutControl.OfferedCard;
        else return false;
        return TryConfirmPayment(checkout, control);
    }

    private bool TryConfirmPayment(Component checkout, CheckoutControl control)
    {
        if (checkout == null || IsInOptions())
            return false;
        var customer = ReadCustomer(checkout);
        if (customer == null || !ReadFloat(checkout, "checkoutProductValue", out var total) || total <= 0f)
        {
            Debug("Checkout ignored: customer or valid transaction total is missing.");
            return false;
        }
        if (_guard.IsPending(checkout.GetInstanceID(), customer.GetInstanceID()))
        {
            Debug("Checkout transaction already pending; duplicate click ignored.");
            return true;
        }
        if (!HasCashierPermission())
        {
            Debug("Local player lacks cashier permission; payment ignored.");
            return false;
        }
        var cashActive = IsActive(checkout.transform, "CashRegisterCanvas/Container");
        var cardActive = IsActive(checkout.transform, "CreditCardCanvas/Container");
        var cashSelected = control == CheckoutControl.OfferedCash || control == CheckoutControl.CashControls;
        if (cashActive && cardActive || (cashSelected ? cardActive : cashActive))
        {
            Debug("Checkout ignored: aimed control does not match the active payment.");
            return false;
        }
        if (cashActive || cardActive)
        {
            QueuePreparation(checkout, customer, total, cashActive);
            return true;
        }

        // No panel yet: use the exact offered money/card object's normal interaction FSM.
        if (control != CheckoutControl.OfferedCash && control != CheckoutControl.OfferedCard)
        {
            Debug("Checkout ignored: accept the offered money/card first, or aim directly at it.");
            return false;
        }
        var offered = checkout.transform.Find(cashSelected ? "Payments/Payment_Money" : "Payments/Payment_Card");
        var interaction = FindBehaviour(offered);
        if (offered == null || !offered.gameObject.activeInHierarchy || interaction == null)
            return false;
        _pendingCheckout = checkout;
        _pendingCustomer = customer.GetInstanceID();
        _preparedTotal = total;
        _preparedConfirm = null;
        _activationCash = cashSelected;
        _activationAt = Time.unscaledTime;
        _awaitingPanel = true;
        _guard.MarkPending(checkout.GetInstanceID(), _pendingCustomer);
        try
        {
            Invoke(interaction, "SendEvent", new[] { typeof(string) }, "Send_Data");
        }
        catch
        {
            CancelPending();
            throw;
        }
        Debug($"Checkout {(cashSelected ? "cash" : "card")} acceptance requested; waiting for payment panel.");
        return true;
    }

    private bool TryPreparePayment(Component checkout, GameObject customer, float total, bool cashActive)
    {
        Component? confirm;
        _preparedAmount = null;
        _preparedText = null;
        if (cashActive)
        {
            confirm = FindBehaviour(checkout.transform.Find("CashRegister/MoneyGivenBack"));
            if (confirm == null || !ReadFloat(checkout, "moneyToReturn", out var expected) ||
                !ReadFloat(checkout, "currentAmountToReturn", out var current) ||
                !CheckoutPayment.TryCashAdjustment(expected, current, out var adjustment))
            {
                Debug("Checkout ignored: cash confirmation FSM or valid change amounts not found.");
                return false;
            }
            _preparedChange = expected;
            // Use the game's update method, including zero change and corrections to prior input.
            Invoke(checkout, "UpdateCash", new[] { typeof(float) }, adjustment);
            if (!ReadBool(checkout, "allowMoneyReturn") ||
                !ReadFloat(checkout, "currentAmountToReturn", out var actual) ||
                Math.Abs((double)actual - expected) > 0.005d)
                return false;
        }
        else
        {
            var card = checkout.transform.Find("CreditCardCanvas/Container/CreditCardContainer");
            var amount = FindBehaviour(card != null ? card.Find("Amount_Text") : null);
            confirm = FindBehaviour(card != null ? card.Find("ButtonNumber_Accept") : null);
            if (amount == null || confirm == null || !CheckoutPayment.TryCardText(total, out var text) ||
                !TryVariable(amount, "GetFsmString", "BaseString", out var baseString))
            {
                Debug("Checkout ignored: card amount/accept FSM or BaseString not found.");
                return false;
            }
            var property = baseString.GetType().GetProperty("Value");
            if (property == null || !property.CanWrite)
                return false;
            if (!TryVariable(amount, "GetFsmBool", "AmountMatches", out var matches))
                return false;
            var matchProperty = matches.GetType().GetProperty("Value");
            if (matchProperty == null || !matchProperty.CanWrite)
                return false;
            matchProperty.SetValue(matches, false);
            property.SetValue(baseString, text);
            _preparedAmount = amount;
            _preparedText = text;
            // Run the same display/parse/amount-match states used by keypad entry.
            Invoke(amount, "SetState", new[] { typeof(string) }, "BuildString");
        }

        // Reserve this transaction until its payment panel closes, including server latency.
        _pendingCheckout = checkout;
        _pendingCustomer = customer.GetInstanceID();
        _guard.MarkPending(checkout.GetInstanceID(), _pendingCustomer);
        _preparedConfirm = confirm;
        _preparedTotal = total;
        _preparedCash = cashActive;
        _preparedFrame = Time.frameCount;
        _confirmDelay.Start(Time.unscaledTime, _confirmSeconds());
        Debug("Checkout amount prepared; normal confirmation will run after game validation.");
        return true;
    }

    internal bool HasPendingPayment(Component checkout) => _pendingCheckout != null && _pendingCheckout == checkout;

    private static bool IsInOptions() => GameContext.IsInOptions();
    private static bool HasCashierPermission() => GameContext.HasCashierPermission();

    internal static Component? FindBehaviour(Transform? transform)
    {
        return transform?.GetComponents<MonoBehaviour>().FirstOrDefault(c =>
            c != null && c.enabled && c.GetType().Name == "PlayMakerFSM" &&
            GameBridge.TryGetMemberValue(c, "FsmName", out var name) && name is string text && text == "Behaviour");
    }

    private static bool TryVariable(object fsm, string method, string name, out object variable)
    {
        variable = null!;
        if (!GameBridge.TryGetMemberValue(fsm, "FsmVariables", out var variables) || variables == null)
            return false;
        var found = Invoke(variables, method, new[] { typeof(string) }, name);
        if (found == null)
            return false;
        variable = found;
        return true;
    }

    private static GameObject? ReadCustomer(object checkout)
    {
        return GameBridge.TryGetMemberValue(checkout, "currentNPC", out var value) ? value as GameObject : null;
    }

    private static bool IsActive(Transform root, string path)
    {
        var child = root.Find(path);
        return child != null && child.gameObject.activeInHierarchy;
    }

    private static bool ReadBool(object owner, string name)
    {
        return GameBridge.TryGetMemberValue(owner, name, out var value) && value is bool flag && flag;
    }

    private static bool ReadFloat(object owner, string name, out float value)
    {
        value = 0f;
        if (!GameBridge.TryGetMemberValue(owner, name, out var raw) || raw is not float number)
            return false;
        value = number;
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static object? Invoke(object owner, string name, Type[] types, params object[] arguments)
    {
        var method = owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public, null, types, null);
        if (method == null)
            throw new MissingMethodException(owner.GetType().FullName, name);
        return method.Invoke(owner, arguments);
    }

    private void Debug(string message)
    {
        if (_verbose())
            _log.LogInfo("[debug] " + message);
    }
}
