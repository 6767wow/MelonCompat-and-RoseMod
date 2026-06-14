namespace MelonLoader.BepInExCompat;

internal static class CompatUnityDriver
{
    private static readonly List<ManagedCoroutine> Coroutines = new();
    private static readonly List<ManagedCoroutine> PendingAdditions = new();
    private static bool pumping;

    internal static object? StartManagedCoroutine(System.Collections.IEnumerator routine)
    {
        if (routine is null)
            return null;

        var coroutine = new ManagedCoroutine(routine);
        if (pumping)
            PendingAdditions.Add(coroutine);
        else
            Coroutines.Add(coroutine);

        return coroutine;
    }

    internal static void StopManagedCoroutine(object? coroutine)
    {
        if (coroutine is null)
            return;

        for (var i = Coroutines.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(Coroutines[i], coroutine))
                Coroutines.RemoveAt(i);
        }

        for (var i = PendingAdditions.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(PendingAdditions[i], coroutine))
                PendingAdditions.RemoveAt(i);
        }
    }

    internal static void PumpManagedCoroutines()
    {
        if (Coroutines.Count == 0 && PendingAdditions.Count == 0)
            return;

        if (PendingAdditions.Count > 0)
        {
            Coroutines.AddRange(PendingAdditions);
            PendingAdditions.Clear();
        }

        pumping = true;
        try
        {
            for (var i = Coroutines.Count - 1; i >= 0; i--)
            {
                if (!Coroutines[i].MoveNext())
                    Coroutines.RemoveAt(i);
            }
        }
        finally
        {
            pumping = false;
        }
    }

    private sealed class ManagedCoroutine
    {
        private readonly Stack<System.Collections.IEnumerator> enumerators = new();
        private float resumeAtScaledTime;
        private float resumeAtRealtime;
        private object? currentYield;

        public ManagedCoroutine(System.Collections.IEnumerator routine)
        {
            enumerators.Push(routine);
        }

        public bool MoveNext()
        {
            try
            {
                if (IsWaiting())
                    return true;

                while (enumerators.Count > 0)
                {
                    var currentEnumerator = enumerators.Peek();
                    if (!currentEnumerator.MoveNext())
                    {
                        enumerators.Pop();
                        continue;
                    }

                    SetYield(currentEnumerator.Current);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                CompatLog.Warning($"Managed Melon coroutine stopped after exception: {ex}");
                return false;
            }
        }

        private bool IsWaiting()
        {
            if (resumeAtScaledTime > 0f)
            {
                if (UnityEngine.Time.time < resumeAtScaledTime)
                    return true;

                resumeAtScaledTime = 0f;
            }

            if (resumeAtRealtime > 0f)
            {
                if (UnityEngine.Time.realtimeSinceStartup < resumeAtRealtime)
                    return true;

                resumeAtRealtime = 0f;
            }

            if (currentYield is UnityEngine.CustomYieldInstruction customYield && customYield.keepWaiting)
                return true;

            currentYield = null;
            return false;
        }

        private void SetYield(object? yield)
        {
            currentYield = yield;

            if (yield is null)
                return;

            if (yield is System.Collections.IEnumerator nested)
            {
                enumerators.Push(nested);
                currentYield = null;
                return;
            }

            if (yield is UnityEngine.WaitForSeconds)
            {
                resumeAtScaledTime = UnityEngine.Time.time + TryReadFloatMember(yield, "m_Seconds");
                return;
            }

            if (yield.GetType().FullName == "UnityEngine.WaitForSecondsRealtime")
            {
                var waitTime = TryReadFloatMember(yield, "waitTime");
                resumeAtRealtime = UnityEngine.Time.realtimeSinceStartup + Math.Max(0f, waitTime);
            }
        }

        private static float TryReadFloatMember(object value, string memberName)
        {
            try
            {
                var flags = System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic;
                var type = value.GetType();
                var property = type.GetProperty(memberName, flags);
                if (property?.GetValue(value) is float result)
                    return result;

                var field = type.GetField(memberName, flags);
                if (field?.GetValue(value) is float fieldResult)
                    return fieldResult;
            }
            catch
            {
            }

            return 0f;
        }
    }
}
