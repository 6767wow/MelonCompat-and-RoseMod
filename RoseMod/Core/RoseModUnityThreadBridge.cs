using System.Reflection;
using System.Threading;

namespace RoseMod;

internal static class RoseModUnityThreadBridge
{
    private static readonly object Gate = new();
    private static readonly Queue<Action> PendingActions = new();
    private static int installed;
    private static int pumping;

    public static bool Enqueue(Action action, string description)
    {
        lock (Gate)
            PendingActions.Enqueue(action);

        if (!Install())
        {
            lock (Gate)
            {
                var remaining = PendingActions.Where(candidate => !ReferenceEquals(candidate, action)).ToArray();
                PendingActions.Clear();
                foreach (var candidate in remaining)
                    PendingActions.Enqueue(candidate);
            }

            return false;
        }

        RoseModLog.Info($"Deferred {description} until Unity's main-thread frame callback.");
        return true;
    }

    private static bool Install()
    {
        if (Interlocked.CompareExchange(ref installed, 1, 0) != 0)
            return true;

        try
        {
            var postfix = typeof(RoseModUnityThreadBridge).GetMethod(nameof(PumpFromUnityThread), BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException(typeof(RoseModUnityThreadBridge).FullName, nameof(PumpFromUnityThread));
            var patched = 0;
            foreach (var target in FindFrameCallbackTargets())
            {
                if (TryPatchPostfix(target, postfix))
                    patched++;
            }

            if (patched > 0)
            {
                RoseModLog.Info($"Installed Unity main-thread deferred callback bridge on {patched} frame method(s).");
                return true;
            }

            RoseModLog.Warning("No Unity frame method was available for deferred main-thread mod loading.");
            Volatile.Write(ref installed, 0);
            return false;
        }
        catch (Exception ex)
        {
            RoseModLog.Warning($"Failed to install Unity main-thread deferred callback bridge: {ex.Message}");
            Volatile.Write(ref installed, 0);
            return false;
        }
    }

    private static IEnumerable<MethodInfo> FindFrameCallbackTargets()
    {
        var timeType = FindUnityType("UnityEngine.Time");
        if (timeType is null)
            yield break;

        foreach (var methodName in new[] { "get_deltaTime", "get_frameCount", "get_time" })
        {
            var method = timeType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (method is not null)
                yield return method;
        }
    }

    private static Type? FindUnityType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = assembly.GetType(fullName, throwOnError: false);
                if (type is not null)
                    return type;
            }
            catch
            {
            }
        }

        return Type.GetType(fullName + ", UnityEngine.CoreModule", throwOnError: false);
    }

    private static bool TryPatchPostfix(MethodInfo target, MethodInfo postfix)
    {
        try
        {
            var harmonyAssembly = LoadHarmonyAssembly();
            var harmonyType = harmonyAssembly.GetType("HarmonyLib.Harmony", throwOnError: true)!;
            var harmonyMethodType = harmonyAssembly.GetType("HarmonyLib.HarmonyMethod", throwOnError: true)!;
            var harmony = Activator.CreateInstance(harmonyType, "dev.jayde.rosemod.mainthread");
            var harmonyMethod = Activator.CreateInstance(harmonyMethodType, postfix);
            var patchMethod = harmonyType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.Name == "Patch")
                .Select(method => new { Method = method, Parameters = method.GetParameters() })
                .FirstOrDefault(candidate => candidate.Parameters.Length >= 3
                    && typeof(MethodBase).IsAssignableFrom(candidate.Parameters[0].ParameterType)
                    && candidate.Parameters.Any(parameter => parameter.Name == "postfix"))
                ?.Method;
            if (patchMethod is null)
                return false;

            var parameters = patchMethod.GetParameters();
            var args = new object?[parameters.Length];
            args[0] = target;
            for (var i = 1; i < parameters.Length; i++)
            {
                if (parameters[i].Name == "postfix")
                    args[i] = harmonyMethod;
            }

            patchMethod.Invoke(harmony, args);
            return true;
        }
        catch (Exception ex)
        {
            RoseModLog.Warning($"Failed to patch Unity frame callback target {target.DeclaringType?.FullName}.{target.Name}: {Unwrap(ex).Message}");
            return false;
        }
    }

    private static Assembly LoadHarmonyAssembly()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name?.Equals("0Harmony", StringComparison.OrdinalIgnoreCase) == true)
            ?? Assembly.Load("0Harmony");
    }

    private static void PumpFromUnityThread()
    {
        if (Interlocked.Exchange(ref pumping, 1) != 0)
            return;

        try
        {
            while (true)
            {
                Action? action;
                lock (Gate)
                    action = PendingActions.Count == 0 ? null : PendingActions.Dequeue();

                if (action is null)
                    return;

                try
                {
                    RoseModLog.Info("Unity main-thread callback reached; running deferred RoseMod load.");
                    action();
                }
                catch (Exception ex)
                {
                    RoseModLog.Error(Unwrap(ex), "Deferred RoseMod load failed.");
                }
            }
        }
        finally
        {
            Volatile.Write(ref pumping, 0);
        }
    }

    private static Exception Unwrap(Exception exception)
    {
        return exception is TargetInvocationException { InnerException: not null }
            ? exception.InnerException
            : exception;
    }
}
