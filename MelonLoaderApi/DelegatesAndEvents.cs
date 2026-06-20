namespace MelonLoader;

public delegate void LemonAction();
public delegate void LemonAction<in T1>(T1 arg1);
public delegate void LemonAction<in T1, in T2>(T1 arg1, T2 arg2);
public delegate void LemonAction<in T1, in T2, in T3>(T1 arg1, T2 arg2, T3 arg3);
public delegate void LemonAction<in T1, in T2, in T3, in T4>(T1 arg1, T2 arg2, T3 arg3, T4 arg4);
public delegate void LemonAction<in T1, in T2, in T3, in T4, in T5>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5);
public delegate void LemonAction<in T1, in T2, in T3, in T4, in T5, in T6>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6);
public delegate void LemonAction<in T1, in T2, in T3, in T4, in T5, in T6, in T7>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7);
public delegate void LemonAction<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8);
public delegate TResult LemonFunc<out TResult>();
public delegate TResult LemonFunc<in T1, out TResult>(T1 arg1);
public delegate TResult LemonFunc<in T1, in T2, out TResult>(T1 arg1, T2 arg2);
public delegate TResult LemonFunc<in T1, in T2, in T3, out TResult>(T1 arg1, T2 arg2, T3 arg3);
public delegate TResult LemonFunc<in T1, in T2, in T3, in T4, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4);
public delegate TResult LemonFunc<in T1, in T2, in T3, in T4, in T5, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5);
public delegate TResult LemonFunc<in T1, in T2, in T3, in T4, in T5, in T6, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6);
public delegate TResult LemonFunc<in T1, in T2, in T3, in T4, in T5, in T6, in T7, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7);
public delegate TResult LemonFunc<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8);

public class MelonEventBase<T>
    where T : Delegate
{
    private readonly List<Subscriber> subscribers = new();

    protected MelonEventBase(bool oneTimeUse = false)
    {
        this.oneTimeUse = oneTimeUse;
    }

    public bool Disposed { get; private set; }

    public bool oneTimeUse { get; }

    public void Subscribe(T subscriber, int priority = 0, bool unsubscribeOnFirstInvocation = false)
    {
        if (Disposed)
            return;

        subscribers.RemoveAll(s => s.Delegate == subscriber);
        subscribers.Add(new Subscriber(subscriber, priority, unsubscribeOnFirstInvocation));
        subscribers.Sort(static (left, right) => right.Priority.CompareTo(left.Priority));
    }

    public void Unsubscribe(T subscriber)
    {
        subscribers.RemoveAll(s => s.Delegate == subscriber);
    }

    public void Unsubscribe(System.Reflection.MethodInfo method, object? target)
    {
        subscribers.RemoveAll(s => s.Delegate.Method == method && Equals(s.Delegate.Target, target));
    }

    public void UnsubscribeAll()
    {
        subscribers.Clear();
    }

    public bool CheckIfSubscribed(System.Reflection.MethodInfo method, object? target)
    {
        return subscribers.Any(s => s.Delegate.Method == method && Equals(s.Delegate.Target, target));
    }

    public void Dispose()
    {
        subscribers.Clear();
        Disposed = true;
    }

    public MelonEventSubscriber<T>[] GetSubscribers()
    {
        return subscribers.Select(s => new MelonEventSubscriber<T>(s.Delegate, s.Priority, s.UnsubscribeOnFirstInvocation)).ToArray();
    }

    public sealed class MelonEventSubscriber
    {
        public MelonEventSubscriber(T subscriber, int priority, bool unsubscribeOnFirstInvocation)
        {
            Subscriber = subscriber;
            Priority = priority;
            UnsubscribeOnFirstInvocation = unsubscribeOnFirstInvocation;
        }

        public T Subscriber { get; }
        public int Priority { get; }
        public bool UnsubscribeOnFirstInvocation { get; }
    }

    protected void InvokeSubscribers(params object?[] args)
    {
        if (Disposed)
            return;

        foreach (var subscriber in subscribers.ToArray())
        {
            try
            {
                subscriber.Delegate.DynamicInvoke(args);
            }
            catch (Exception ex)
            {
                BepInExCompat.CompatLog.Error(ex, $"MelonEvent subscriber {subscriber.Delegate.Method.DeclaringType?.FullName}.{subscriber.Delegate.Method.Name} failed.");
            }

            if (subscriber.UnsubscribeOnFirstInvocation || oneTimeUse)
                subscribers.Remove(subscriber);
        }
    }

    public sealed class MelonEventSubscriber<TDelegate>
        where TDelegate : Delegate
    {
        public MelonEventSubscriber(TDelegate subscriber, int priority, bool unsubscribeOnFirstInvocation)
        {
            Subscriber = subscriber;
            Priority = priority;
            UnsubscribeOnFirstInvocation = unsubscribeOnFirstInvocation;
        }

        public TDelegate Subscriber { get; }
        public int Priority { get; }
        public bool UnsubscribeOnFirstInvocation { get; }
    }

    private sealed class Subscriber
    {
        public Subscriber(T subscriber, int priority, bool unsubscribeOnFirstInvocation)
        {
            Delegate = subscriber;
            Priority = priority;
            UnsubscribeOnFirstInvocation = unsubscribeOnFirstInvocation;
        }

        public T Delegate { get; }
        public int Priority { get; }
        public bool UnsubscribeOnFirstInvocation { get; }
    }
}

public sealed class MelonEvent : MelonEventBase<LemonAction>
{
    public MelonEvent(bool oneTimeUse = false)
        : base(oneTimeUse)
    {
    }

    public void Invoke() => InvokeSubscribers();
}

public sealed class MelonEvent<T1> : MelonEventBase<LemonAction<T1>>
{
    public MelonEvent(bool oneTimeUse = false)
        : base(oneTimeUse)
    {
    }

    public void Invoke(T1 arg1) => InvokeSubscribers(arg1);
}

public sealed class MelonEvent<T1, T2> : MelonEventBase<LemonAction<T1, T2>>
{
    public MelonEvent(bool oneTimeUse = false)
        : base(oneTimeUse)
    {
    }

    public void Invoke(T1 arg1, T2 arg2) => InvokeSubscribers(arg1, arg2);
}

public sealed class MelonEvent<T1, T2, T3> : MelonEventBase<LemonAction<T1, T2, T3>>
{
    public MelonEvent(bool oneTimeUse = false)
        : base(oneTimeUse)
    {
    }

    public void Invoke(T1 arg1, T2 arg2, T3 arg3) => InvokeSubscribers(arg1, arg2, arg3);
}

public sealed class MelonEvent<T1, T2, T3, T4> : MelonEventBase<LemonAction<T1, T2, T3, T4>>
{
    public MelonEvent(bool oneTimeUse = false)
        : base(oneTimeUse)
    {
    }

    public void Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4) => InvokeSubscribers(arg1, arg2, arg3, arg4);
}

public sealed class MelonEvent<T1, T2, T3, T4, T5> : MelonEventBase<LemonAction<T1, T2, T3, T4, T5>>
{
    public MelonEvent(bool oneTimeUse = false)
        : base(oneTimeUse)
    {
    }

    public void Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5) => InvokeSubscribers(arg1, arg2, arg3, arg4, arg5);
}

public sealed class MelonEvent<T1, T2, T3, T4, T5, T6> : MelonEventBase<LemonAction<T1, T2, T3, T4, T5, T6>>
{
    public MelonEvent(bool oneTimeUse = false)
        : base(oneTimeUse)
    {
    }

    public void Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6) => InvokeSubscribers(arg1, arg2, arg3, arg4, arg5, arg6);
}

public sealed class MelonEvent<T1, T2, T3, T4, T5, T6, T7> : MelonEventBase<LemonAction<T1, T2, T3, T4, T5, T6, T7>>
{
    public MelonEvent(bool oneTimeUse = false)
        : base(oneTimeUse)
    {
    }

    public void Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7) => InvokeSubscribers(arg1, arg2, arg3, arg4, arg5, arg6, arg7);
}

public sealed class MelonEvent<T1, T2, T3, T4, T5, T6, T7, T8> : MelonEventBase<LemonAction<T1, T2, T3, T4, T5, T6, T7, T8>>
{
    public MelonEvent(bool oneTimeUse = false)
        : base(oneTimeUse)
    {
    }

    public void Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8) => InvokeSubscribers(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
}

public static class MelonEvents
{
    public static readonly MelonEvent OnPreInitialization = new();
    public static readonly MelonEvent OnApplicationEarlyStart = new();
    public static readonly MelonEvent OnPreSupportModule = new();
    public static readonly MelonEvent OnApplicationStart = new();
    public static readonly MelonEvent OnApplicationLateStart = new();
    public static readonly MelonEvent OnApplicationDefiniteQuit = new();
    public static readonly MelonEvent OnApplicationQuit = new();
    public static readonly MelonEvent OnUpdate = new();
    public static readonly MelonEvent OnFixedUpdate = new();
    public static readonly MelonEvent OnLateUpdate = new();
    public static readonly MelonEvent OnGUI = new();
    public static readonly MelonEvent<int, string> OnSceneWasLoaded = new();
    public static readonly MelonEvent<int, string> OnSceneWasInitialized = new();
    public static readonly MelonEvent<int, string> OnSceneWasUnloaded = new();
    public static readonly MelonEvent OnPreModsLoaded = new();
}
