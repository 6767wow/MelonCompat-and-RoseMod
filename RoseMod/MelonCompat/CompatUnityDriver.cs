namespace MelonLoader.BepInExCompat;

internal static class CompatUnityDriver
{
    private static readonly List<ManagedCoroutine> Coroutines = new();

    internal static object? StartManagedCoroutine(System.Collections.IEnumerator routine)
    {
        if (routine is null)
            return null;

        var coroutine = new ManagedCoroutine(routine);
        Coroutines.Add(coroutine);
        return coroutine;
    }

    internal static void StopManagedCoroutine(object? coroutine)
    {
        if (coroutine is null)
            return;

        Coroutines.RemoveAll(item => ReferenceEquals(item, coroutine));
    }

    public static void PumpManagedCoroutines()
    {
        for (var i = Coroutines.Count - 1; i >= 0; i--)
        {
            if (!Coroutines[i].MoveNext())
                Coroutines.RemoveAt(i);
        }
    }

    private sealed class ManagedCoroutine
    {
        private readonly Stack<System.Collections.IEnumerator> enumerators = new();

        public ManagedCoroutine(System.Collections.IEnumerator routine)
        {
            enumerators.Push(routine);
        }

        public bool MoveNext()
        {
            try
            {
                while (enumerators.Count > 0)
                {
                    var current = enumerators.Peek();
                    if (!current.MoveNext())
                    {
                        enumerators.Pop();
                        continue;
                    }

                    if (current.Current is System.Collections.IEnumerator nested)
                        enumerators.Push(nested);

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                CompatLog.Warning("Managed coroutine stopped after exception: " + ex);
                return false;
            }
        }
    }
}
