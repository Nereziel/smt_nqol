using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace SMT.PriceGun200;

internal sealed class CheckoutColliderRepair : IDisposable
{
    private sealed class Patch
    {
        internal BoxCollider Original = null!;
        internal MeshCollider Replacement = null!;
        internal Mesh Mesh = null!;
        internal bool OriginalEnabled;
        internal Vector3 Center;
        internal Vector3 Size;
    }

    private readonly ManualLogSource _log;
    private readonly Dictionary<int, Patch> _patches = new();
    private readonly List<int> _remove = new();
    private Type? _containerType;
    private float _nextScan;

    internal CheckoutColliderRepair(ManualLogSource log) { _log = log; }

    internal void Tick(bool enabled)
    {
        if (!enabled)
        {
            Dispose();
            return;
        }
        if (Time.unscaledTime < _nextScan)
            return;
        _nextScan = Time.unscaledTime + 2f;
        MaintainPatches();
        _containerType ??= GameBridge.FindType("Data_Container");
        if (_containerType == null)
            return;
        var repaired = 0;
        foreach (var item in UnityEngine.Object.FindObjectsByType(_containerType, FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (item is not Component container || container == null || !container.gameObject.scene.IsValid())
                continue;
            // Match actual checkouts, excluding shelves, storage and unrelated game objects.
            if (container.transform.Find("CashRegisterCanvas") == null || container.transform.Find("CreditCardCanvas") == null)
                continue;
            foreach (var box in container.GetComponentsInChildren<BoxCollider>(true))
            {
                if (box == null || _patches.ContainsKey(box.GetInstanceID()) ||
                    !CheckoutColliderGeometry.NeedsReplacement(box.size, box.transform.lossyScale))
                    continue;
                if (TryPatch(box)) repaired++;
            }
        }
        if (repaired != 0)
            _log.LogInfo($"Checkout collider repair: replaced {repaired} invalid box colliders with convex boxes; transforms unchanged.");
    }

    private bool TryPatch(BoxCollider box)
    {
        if (!CheckoutColliderGeometry.TryVertices(box.center, box.size, out var vertices))
            return false;
        var mesh = new Mesh { name = "SMT repaired checkout box", hideFlags = HideFlags.HideAndDontSave };
        MeshCollider? replacement = null;
        var wasEnabled = box.enabled;
        try
        {
            mesh.vertices = vertices;
            mesh.triangles = CheckoutColliderGeometry.Triangles;
            mesh.RecalculateBounds();
            replacement = box.gameObject.AddComponent<MeshCollider>();
            replacement.enabled = false;
            replacement.convex = true;
            replacement.sharedMesh = mesh;
            replacement.isTrigger = box.isTrigger;
            replacement.sharedMaterial = box.sharedMaterial;
            replacement.contactOffset = box.contactOffset;
            var patch = new Patch { Original = box, Replacement = replacement, Mesh = mesh,
                OriginalEnabled = wasEnabled, Center = box.center, Size = box.size };
            _patches.Add(box.GetInstanceID(), patch);
            box.enabled = false;
            replacement.enabled = wasEnabled;
            return true;
        }
        catch
        {
            _patches.Remove(box.GetInstanceID());
            if (replacement != null)
            {
                replacement.enabled = false;
                UnityEngine.Object.Destroy(replacement);
            }
            UnityEngine.Object.Destroy(mesh);
            if (box != null) box.enabled = wasEnabled;
            throw;
        }
    }

    private void MaintainPatches()
    {
        _remove.Clear();
        foreach (var entry in _patches)
        {
            var patch = entry.Value;
            var box = patch.Original;
            if (box == null || patch.Replacement == null ||
                !CheckoutColliderGeometry.NeedsReplacement(box.size, box.transform.lossyScale))
            {
                Restore(patch);
                _remove.Add(entry.Key);
                continue;
            }
            if (patch.Center != box.center || patch.Size != box.size)
            {
                if (!CheckoutColliderGeometry.TryVertices(box.center, box.size, out var vertices))
                {
                    Restore(patch);
                    _remove.Add(entry.Key);
                    continue;
                }
                patch.Replacement.sharedMesh = null;
                patch.Mesh.vertices = vertices;
                patch.Mesh.RecalculateBounds();
                patch.Replacement.sharedMesh = patch.Mesh;
                patch.Center = box.center;
                patch.Size = box.size;
            }
            patch.Replacement.isTrigger = box.isTrigger;
            patch.Replacement.sharedMaterial = box.sharedMaterial;
            patch.Replacement.contactOffset = box.contactOffset;
            // Some game operations re-enable all colliders after moving a register.
            if (box.enabled)
            {
                patch.Replacement.enabled = true;
                box.enabled = false;
            }
        }
        foreach (var id in _remove) _patches.Remove(id);
    }

    private static void Restore(Patch patch)
    {
        var enabled = patch.Replacement != null ? patch.Replacement.enabled : patch.OriginalEnabled;
        if (patch.Replacement != null)
        {
            patch.Replacement.enabled = false;
            UnityEngine.Object.Destroy(patch.Replacement);
        }
        if (patch.Mesh != null) UnityEngine.Object.Destroy(patch.Mesh);
        if (patch.Original != null) patch.Original.enabled = enabled;
    }

    public void Dispose()
    {
        foreach (var patch in _patches.Values) Restore(patch);
        _patches.Clear();
        _nextScan = 0f;
    }
}
