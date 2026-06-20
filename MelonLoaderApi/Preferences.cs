namespace MelonLoader.Preferences
{
    public class ValueValidator
    {
        public virtual bool IsValid(object? value) => true;
        public virtual object? EnsureValid(object? value) => value;
    }

    public interface IValueRange
    {
        object? Min { get; }
        object? Max { get; }
    }

    public class ValueRange<T> : ValueValidator, IValueRange
        where T : IComparable
    {
        public ValueRange(T minValue, T maxValue)
        {
            MinValue = minValue;
            MaxValue = maxValue;
        }

        public T MinValue { get; }
        public T MaxValue { get; }
        public object? Min => MinValue;
        public object? Max => MaxValue;

        public override bool IsValid(object? value)
        {
            return value is T typed && typed.CompareTo(MinValue) >= 0 && typed.CompareTo(MaxValue) <= 0;
        }

        public override object? EnsureValid(object? value)
        {
            if (value is not T typed)
                return MinValue;

            if (typed.CompareTo(MinValue) < 0)
                return MinValue;

            return typed.CompareTo(MaxValue) > 0 ? MaxValue : typed;
        }
    }

    public class MelonPreferences_ReflectiveCategory : MelonLoader.MelonPreferences_Category
    {
        public MelonPreferences_ReflectiveCategory(string identifier, string displayName)
            : base(identifier, displayName, false, false)
        {
        }
    }
}

namespace MelonLoader
{
using MelonLoader.Preferences;

public static class MelonPreferences
{
    public static readonly List<MelonPreferences_Category> Categories = new();
    public static readonly List<MelonPreferences_ReflectiveCategory> ReflectiveCategories = new();
    public static readonly MelonEvent<string> OnPreferencesSaved = new();
    public static readonly MelonEvent<string> OnPreferencesLoaded = new();

    public static MelonPreferences_Category CreateCategory(string identifier) => CreateCategory(identifier, identifier);

    public static MelonPreferences_Category CreateCategory(string identifier, string displayName) => CreateCategory(identifier, displayName, false, false);

    public static MelonPreferences_Category CreateCategory(string identifier, string displayName, bool isHidden, bool isInlined)
    {
        var existing = GetCategory(identifier);
        if (existing is not null)
            return existing;

        var category = new MelonPreferences_Category(identifier, displayName, isHidden, isInlined);
        Categories.Add(category);
        return category;
    }

    public static T CreateCategory<T>(string identifier, string displayName)
        where T : MelonPreferences_ReflectiveCategory
    {
        var category = (T)Activator.CreateInstance(typeof(T), identifier, displayName)!;
        ReflectiveCategories.Add(category);
        Categories.Add(category);
        return category;
    }

    public static MelonPreferences_Entry CreateEntry<T>(string category, string identifier, T defaultValue, string displayName, bool isHidden = false)
    {
        return CreateCategory(category).CreateEntry(identifier, defaultValue, displayName, string.Empty, isHidden, false, null);
    }

    public static MelonPreferences_Entry<T> CreateEntry<T>(
        string category,
        string identifier,
        T defaultValue,
        string displayName = "",
        string description = "",
        bool isHidden = false,
        bool dontSaveDefault = false,
        ValueValidator? validator = null)
    {
        return CreateCategory(category).CreateEntry(identifier, defaultValue, displayName, description, isHidden, dontSaveDefault, validator);
    }

    public static MelonPreferences_Category? GetCategory(string identifier)
    {
        return Categories.FirstOrDefault(category => string.Equals(category.Identifier, identifier, StringComparison.OrdinalIgnoreCase));
    }

    public static T? GetCategory<T>(string identifier)
        where T : MelonPreferences_Category
    {
        return GetCategory(identifier) as T;
    }

    public static MelonPreferences_Entry? GetEntry(string category, string identifier) => GetCategory(category)?.GetEntry(identifier);

    public static MelonPreferences_Entry<T>? GetEntry<T>(string category, string identifier) => GetCategory(category)?.GetEntry<T>(identifier);

    public static T? GetEntryValue<T>(string category, string identifier) => GetEntry<T>(category, identifier) is { } entry ? entry.Value : default;

    public static void SetEntryValue<T>(string category, string identifier, T value)
    {
        if (GetEntry<T>(category, identifier) is { } entry)
            entry.Value = value;
    }

    public static bool HasEntry(string category, string identifier) => GetEntry(category, identifier) is not null;

    public static void Save()
    {
        OnPreferencesSaved.Invoke(Path.Combine(MelonUtils.UserDataDirectory, "MelonPreferences.cfg"));
    }

    public static void Load()
    {
        OnPreferencesLoaded.Invoke(Path.Combine(MelonUtils.UserDataDirectory, "MelonPreferences.cfg"));
    }

    public static void SaveCategory(string category, bool printmsg = false) => Save();

    public static void RemoveCategoryFromFile(string category, string filepath)
    {
    }
}

public class MelonPreferences_Category
{
    public MelonPreferences_Category(string identifier, string displayName, bool isHidden, bool isInlined)
    {
        Identifier = identifier;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? identifier : displayName;
        IsHidden = isHidden;
        IsInlined = isInlined;
    }

    public string Identifier { get; }
    public string DisplayName { get; set; }
    public bool IsHidden { get; set; }
    public bool IsInlined { get; set; }
    public List<MelonPreferences_Entry> Entries { get; } = new();

    public MelonPreferences_Entry CreateEntry<T>(string identifier, T defaultValue, string displayName = "", bool isHidden = false)
    {
        return CreateEntry(identifier, defaultValue, displayName, string.Empty, isHidden, false, null);
    }

    public MelonPreferences_Entry<T> CreateEntry<T>(
        string identifier,
        T defaultValue,
        string displayName = "",
        string description = "",
        bool isHidden = false,
        bool dontSaveDefault = false,
        ValueValidator? validator = null,
        string? oldIdentifier = null)
    {
        if (GetEntry<T>(identifier) is { } existing)
            return existing;

        var entry = new MelonPreferences_Entry<T>
        {
            Category = this,
            Identifier = identifier,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? identifier : displayName,
            Description = description ?? string.Empty,
            IsHidden = isHidden,
            DontSaveDefault = dontSaveDefault,
            Validator = validator,
            DefaultValue = defaultValue,
            Value = defaultValue,
            EditedValue = defaultValue
        };

        Entries.Add(entry);
        return entry;
    }

    public MelonPreferences_Entry? GetEntry(string identifier)
    {
        return Entries.FirstOrDefault(entry => string.Equals(entry.Identifier, identifier, StringComparison.OrdinalIgnoreCase));
    }

    public MelonPreferences_Entry<T>? GetEntry<T>(string identifier) => GetEntry(identifier) as MelonPreferences_Entry<T>;

    public bool HasEntry(string identifier) => GetEntry(identifier) is not null;

    public bool DeleteEntry(string identifier)
    {
        var entry = GetEntry(identifier);
        return entry is not null && Entries.Remove(entry);
    }

    public bool RenameEntry(string identifier, string newIdentifier)
    {
        var entry = GetEntry(identifier);
        if (entry is null)
            return false;
        entry.Identifier = newIdentifier;
        return true;
    }

    public void SetFilePath(string filepath)
    {
    }

    public void SetFilePath(string filepath, bool autoload)
    {
    }

    public void SetFilePath(string filepath, bool autoload, bool printmsg)
    {
    }

    public void ResetFilePath()
    {
    }

    public void SaveToFile(bool printmsg = false)
    {
        MelonPreferences.Save();
    }

    public void LoadFromFile(bool printmsg = false)
    {
        MelonPreferences.Load();
    }

    public void DestroyFileWatcher()
    {
    }
}

public class MelonPreferences_Entry
{
    public readonly MelonEvent<object?, object?> OnEntryValueChangedUntyped = new();

    public MelonPreferences_Category? Category { get; internal set; }
    public string Identifier { get; internal set; } = string.Empty;
    public string DisplayName { get; internal set; } = string.Empty;
    public string Description { get; internal set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public bool IsHidden { get; internal set; }
    public bool DontSaveDefault { get; internal set; }
    public ValueValidator? Validator { get; internal set; }
    public virtual object? BoxedValue { get; set; }
    public virtual object? BoxedEditedValue { get; set; }

    public virtual Type GetReflectedType() => typeof(object);
    public virtual string GetValueAsString() => BoxedValue?.ToString() ?? string.Empty;
    public virtual string GetEditedValueAsString() => BoxedEditedValue?.ToString() ?? string.Empty;
    public virtual string GetDefaultValueAsString() => string.Empty;
    public virtual string GetExceptionMessage(string value) => string.Empty;
    public virtual void ResetToDefault() { }
}

public class MelonPreferences_Entry<T> : MelonPreferences_Entry
{
    private T? value;
    private T? editedValue;

    public readonly MelonEvent<T?, T?> OnEntryValueChanged = new();

    public T? DefaultValue { get; internal set; }

    public T? Value
    {
        get => value;
        set
        {
            var old = this.value;
            this.value = value;
            OnEntryValueChanged.Invoke(old, value);
            OnEntryValueChangedUntyped.Invoke(old, value);
        }
    }

    public T? EditedValue
    {
        get => editedValue;
        set => editedValue = value;
    }

    public override object? BoxedValue
    {
        get => Value;
        set => Value = value is T typed ? typed : default;
    }

    public override object? BoxedEditedValue
    {
        get => EditedValue;
        set => EditedValue = value is T typed ? typed : default;
    }

    public override Type GetReflectedType() => typeof(T);
    public override string GetValueAsString() => Value?.ToString() ?? string.Empty;
    public override string GetEditedValueAsString() => EditedValue?.ToString() ?? string.Empty;
    public override string GetDefaultValueAsString() => DefaultValue?.ToString() ?? string.Empty;

    public override void ResetToDefault()
    {
        Value = DefaultValue;
        EditedValue = DefaultValue;
    }
}

public class MelonPrefs
{
    private static readonly Dictionary<string, Dictionary<string, MelonPreference>> Preferences = new(StringComparer.OrdinalIgnoreCase);

    public enum MelonPreferenceType
    {
        STRING,
        BOOL,
        INT,
        FLOAT
    }

    public class MelonPreference
    {
        public object? Value { get; set; }
        public object? ValueEdited { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsHidden { get; set; }
        public string DisplayText => Description;
        public MelonPreferenceType Type => Value switch
        {
            bool => MelonPreferenceType.BOOL,
            int or long or byte => MelonPreferenceType.INT,
            float or double or decimal => MelonPreferenceType.FLOAT,
            _ => MelonPreferenceType.STRING
        };
    }

    public static Dictionary<string, Dictionary<string, MelonPreference>> GetPreferences() => Preferences;

    public static void RegisterCategory(string category, string displayName = "")
    {
        EnsureCategory(category);
        MelonPreferences.CreateCategory(category, displayName);
    }

    public static string GetCategoryDisplayName(string category) => MelonPreferences.GetCategory(category)?.DisplayName ?? category;

    public static void RegisterString(string category, string name, string defaultValue, string description = "", bool isHidden = false) => Register(category, name, defaultValue, description, isHidden);
    public static void RegisterBool(string category, string name, bool defaultValue, string description = "", bool isHidden = false) => Register(category, name, defaultValue, description, isHidden);
    public static void RegisterInt(string category, string name, int defaultValue, string description = "", bool isHidden = false) => Register(category, name, defaultValue, description, isHidden);
    public static void RegisterFloat(string category, string name, float defaultValue, string description = "", bool isHidden = false) => Register(category, name, defaultValue, description, isHidden);

    public static string GetString(string category, string name) => Convert.ToString(Get(category, name)) ?? string.Empty;
    public static bool GetBool(string category, string name) => Convert.ToBoolean(Get(category, name));
    public static int GetInt(string category, string name) => Convert.ToInt32(Get(category, name));
    public static float GetFloat(string category, string name) => Convert.ToSingle(Get(category, name));

    public static void SetString(string category, string name, string value) => Set(category, name, value);
    public static void SetBool(string category, string name, bool value) => Set(category, name, value);
    public static void SetInt(string category, string name, int value) => Set(category, name, value);
    public static void SetFloat(string category, string name, float value) => Set(category, name, value);

    public static bool HasKey(string category, string name) => Preferences.TryGetValue(category, out var entries) && entries.ContainsKey(name);

    public static void SaveConfig() => MelonPreferences.Save();

    private static void Register<T>(string category, string name, T defaultValue, string description, bool isHidden)
    {
        var entries = EnsureCategory(category);
        entries[name] = new MelonPreference { Value = defaultValue, Description = description, IsHidden = isHidden };
        MelonPreferences.CreateEntry(category, name, defaultValue, name, description, isHidden);
    }

    private static object? Get(string category, string name)
    {
        return Preferences.TryGetValue(category, out var entries) && entries.TryGetValue(name, out var preference)
            ? preference.Value
            : null;
    }

    private static void Set(string category, string name, object value)
    {
        var entries = EnsureCategory(category);
        if (!entries.TryGetValue(name, out var preference))
            entries[name] = preference = new MelonPreference();
        preference.Value = value;
    }

    private static Dictionary<string, MelonPreference> EnsureCategory(string category)
    {
        if (!Preferences.TryGetValue(category, out var entries))
            Preferences[category] = entries = new Dictionary<string, MelonPreference>(StringComparer.OrdinalIgnoreCase);
        return entries;
    }
}

public class ModPrefs : MelonPrefs
{
    public enum PrefType
    {
        STRING,
        BOOL,
        INT,
        FLOAT
    }

    public class PrefDesc : MelonPreference
    {
        public PrefDesc()
        {
        }

        public PrefDesc(MelonPreference preference)
        {
            Value = preference.Value?.ToString() ?? string.Empty;
            ValueEdited = preference.ValueEdited?.ToString() ?? Value;
            DisplayText = preference.DisplayText;
            Hidden = preference.IsHidden;
            Type = (PrefType)preference.Type;
        }

        public new string Value { get; set; } = string.Empty;
        public new string ValueEdited { get; set; } = string.Empty;
        public new string DisplayText { get; set; } = string.Empty;
        public bool Hidden { get; set; }
        public new PrefType Type { get; set; }
    }

    public static Dictionary<string, Dictionary<string, PrefDesc>> GetPrefs()
    {
        return GetPreferences().ToDictionary(
            category => category.Key,
            category => category.Value.ToDictionary(entry => entry.Key, entry => new PrefDesc(entry.Value)),
            StringComparer.OrdinalIgnoreCase);
    }

    public static void RegisterPrefString(string section, string name, string defaultValue, string displayText = "", bool hideFromList = false) => RegisterString(section, name, defaultValue, displayText, hideFromList);
    public static void RegisterPrefBool(string section, string name, bool defaultValue, string displayText = "", bool hideFromList = false) => RegisterBool(section, name, defaultValue, displayText, hideFromList);
    public static void RegisterPrefInt(string section, string name, int defaultValue, string displayText = "", bool hideFromList = false) => RegisterInt(section, name, defaultValue, displayText, hideFromList);
    public static void RegisterPrefFloat(string section, string name, float defaultValue, string displayText = "", bool hideFromList = false) => RegisterFloat(section, name, defaultValue, displayText, hideFromList);
}
}
