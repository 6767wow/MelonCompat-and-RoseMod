#if !BEPINEX_MONO
using System.Reflection;
using UnityEngine;

namespace MelonLoader.BepInExCompat;

public static class UnityPhysicsCompatibility
{
    private const float BaldiDayNotificationDedupSeconds = 0.75f;

    private static readonly object BaldiNotifyGate = new();
    private static readonly Dictionary<string, float> LastBaldiDayNotifications = new(StringComparer.Ordinal);
    private static MethodInfo? baldiNotifyDayTransition;
    private static bool baldiNotifyLookupComplete;

    public static void SetLinearVelocity(Rigidbody? body, Vector3 value)
    {
        if (body == null || body.isKinematic)
            return;

        body.linearVelocity = value;
    }

    public static void SetAngularVelocity(Rigidbody? body, Vector3 value)
    {
        if (body == null || body.isKinematic)
            return;

        body.angularVelocity = value;
    }

    public static Vector3 ClosestPoint(Collider? collider, Vector3 position)
    {
        if (collider == null)
            return position;

        try
        {
            if (SupportsPreciseClosestPoint(collider))
                return collider.ClosestPoint(position);
        }
        catch
        {
        }

        try
        {
            return collider.bounds.ClosestPoint(position);
        }
        catch
        {
        }

        try
        {
            return collider.transform != null ? collider.transform.position : position;
        }
        catch
        {
            return position;
        }
    }

    public static Vector3 PhysicsClosestPoint(Vector3 point, Collider? collider, Vector3 position, Quaternion rotation)
    {
        if (collider == null)
            return position;

        try
        {
            if (SupportsPreciseClosestPoint(collider))
                return Physics.ClosestPoint(point, collider, position, rotation);
        }
        catch
        {
        }

        return ClosestPoint(collider, point);
    }

    public static Coroutine? StartCoroutineString(MonoBehaviour? behaviour, string? methodName)
    {
        if (behaviour == null || string.IsNullOrEmpty(methodName))
            return null;

        TryNotifyBaldiDayTransition(behaviour, methodName);
        return behaviour.StartCoroutine(methodName);
    }

    public static bool NotifyBaldiDayTransition(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return false;

        try
        {
            var method = GetBaldiNotifyDayTransition();
            if (method is null)
                return false;

            var now = GetRealtimeSinceStartupSafe();
            lock (BaldiNotifyGate)
            {
                if (LastBaldiDayNotifications.TryGetValue(reason, out var last)
                    && now - last < BaldiDayNotificationDedupSeconds)
                    return false;

                LastBaldiDayNotifications[reason] = now;
            }

            method.Invoke(null, new object[] { reason });
            CompatLog.Info($"Published Baldi Helps Granny day-transition substitute: {reason}.");
            return true;
        }
        catch (Exception ex)
        {
            CompatLog.Warning($"Baldi day-transition substitute notification failed: {ex.Message}");
            return false;
        }
    }

    private static bool SupportsPreciseClosestPoint(Collider collider)
    {
        return collider is BoxCollider
            || collider is SphereCollider
            || collider is CapsuleCollider
            || collider is MeshCollider meshCollider && meshCollider.convex;
    }

    private static void TryNotifyBaldiDayTransition(MonoBehaviour behaviour, string methodName)
    {
        string? reason = null;
        var behaviourTypeName = behaviour.GetType().Name;
        if (string.Equals(methodName, "EndDay", StringComparison.Ordinal)
            && string.Equals(behaviourTypeName, "endDay", StringComparison.Ordinal))
            reason = "endDay.EndDay";
        else if (string.Equals(methodName, "newDay", StringComparison.Ordinal)
            && string.Equals(behaviourTypeName, "startNewDay", StringComparison.Ordinal))
            reason = "startNewDay.newDay";

        if (reason is null)
            return;

        NotifyBaldiDayTransition(reason);
    }

    private static MethodInfo? GetBaldiNotifyDayTransition()
    {
        lock (BaldiNotifyGate)
        {
            if (baldiNotifyLookupComplete)
                return baldiNotifyDayTransition;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? type;
                try
                {
                    type = assembly.GetType("BaldiHelpsGranny.BaldiEnemy", throwOnError: false);
                }
                catch
                {
                    continue;
                }

                baldiNotifyDayTransition = type?.GetMethod(
                    "NotifyDayTransition",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(string) },
                    null);
                if (baldiNotifyDayTransition is not null)
                    break;
            }

            baldiNotifyLookupComplete = true;
            return baldiNotifyDayTransition;
        }
    }

    private static float GetRealtimeSinceStartupSafe()
    {
        try
        {
            return Time.realtimeSinceStartup;
        }
        catch
        {
            return Environment.TickCount / 1000f;
        }
    }
}
#endif
