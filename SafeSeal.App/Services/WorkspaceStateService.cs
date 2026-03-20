using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SafeSeal.App.Services;

public sealed class WorkspaceStateService
{
    private static readonly HashSet<string> ValidValidityModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "None",
        "Date",
        "ExpiryDate",
    };

    
    private static readonly HashSet<string> ValidDatePresets = new(StringComparer.OrdinalIgnoreCase)
    {
        "Today",
        "ThisWeek",
        "ThisMonth",
        "Custom",
    };

    private static readonly HashSet<string> ValidDateDisplayFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "Iso",
        "EnglishShortMonth",
        "Slash",
    };

    private readonly AppPreferenceStore _store;
    private readonly object _sync = new();
    private readonly ObservableCollection<WorkspaceTemplateFieldState> _currentTemplateFields = [];
    private bool _initialized;
    private bool _suppressTemplateFieldEvents;
    private WorkspacePreferences _workspace = WorkspacePreferences.CreateDefault();

    public WorkspaceStateService(AppPreferenceStore store)
    {
        _store = store;
    }

    public static WorkspaceStateService Instance { get; } = new(new AppPreferenceStore());

    public event EventHandler? WorkspaceChanged;

    public ObservableCollection<WorkspaceTemplateFieldState> CurrentTemplateFields => _currentTemplateFields;

    public void Initialize()
    {
        lock (_sync)
        {
            if (_initialized)
            {
                return;
            }

            AppPreferences preferences = _store.Load();
            _workspace = Normalize(preferences.Workspace).DeepCopy();
            _initialized = true;
        }
    }

    public WorkspacePreferences GetSnapshot()
    {
        lock (_sync)
        {
            return _workspace.DeepCopy();
        }
    }

    public Dictionary<string, string> GetTemplateFieldValuesSnapshot()
    {
        lock (_sync)
        {
            return new Dictionary<string, string>(
                _workspace.TemplateFieldValues,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public void ConfigureTemplateFields(IEnumerable<WorkspaceTemplateFieldState> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        List<WorkspaceTemplateFieldState> configured;

        lock (_sync)
        {
            Dictionary<string, string> persisted = _workspace.TemplateFieldValues;

            configured = new List<WorkspaceTemplateFieldState>();
            foreach (WorkspaceTemplateFieldState field in fields)
            {
                if (string.IsNullOrWhiteSpace(field.Key))
                {
                    continue;
                }

                string key = field.Key.Trim();
                string value = persisted.TryGetValue(key, out string? existing)
                    ? existing
                    : field.Value;

                configured.Add(new WorkspaceTemplateFieldState(key, field.Label, value));
            }

            ReplaceTemplateFieldsLocked(configured);
            _workspace = _workspace with
            {
                TemplateFieldValues = SnapshotTemplateFieldValuesLocked(),
            };
        }

        OnWorkspaceChanged();
    }

    public void ApplyTemplateFieldValues(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        bool changed = false;

        lock (_sync)
        {
            if (_currentTemplateFields.Count == 0)
            {
                _workspace = _workspace with
                {
                    TemplateFieldValues = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase),
                };

                changed = true;
            }
            else
            {
                _suppressTemplateFieldEvents = true;
                try
                {
                    foreach (WorkspaceTemplateFieldState field in _currentTemplateFields)
                    {
                        if (values.TryGetValue(field.Key, out string? value))
                        {
                            string next = value ?? string.Empty;
                            if (!string.Equals(field.Value, next, StringComparison.Ordinal))
                            {
                                field.Value = next;
                                changed = true;
                            }
                        }
                    }
                }
                finally
                {
                    _suppressTemplateFieldEvents = false;
                }

                if (changed)
                {
                    _workspace = _workspace with
                    {
                        TemplateFieldValues = SnapshotTemplateFieldValuesLocked(),
                    };
                }
            }
        }

        if (changed)
        {
            OnWorkspaceChanged();
        }
    }
    public void UpdateTemplateFieldValue(string key, string? value, bool notify = true)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        string normalizedKey = key.Trim();
        string normalizedValue = value ?? string.Empty;

        lock (_sync)
        {
            _suppressTemplateFieldEvents = true;
            try
            {
                foreach (WorkspaceTemplateFieldState field in _currentTemplateFields)
                {
                    if (!string.Equals(field.Key, normalizedKey, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.Equals(field.Value, normalizedValue, StringComparison.Ordinal))
                    {
                        field.Value = normalizedValue;
                    }

                    break;
                }
            }
            finally
            {
                _suppressTemplateFieldEvents = false;
            }

            Dictionary<string, string> snapshot = SnapshotTemplateFieldValuesLocked();
            snapshot[normalizedKey] = normalizedValue;
            _workspace = _workspace with
            {
                TemplateFieldValues = snapshot,
            };
        }

        if (notify)
        {
            OnWorkspaceChanged();
        }
    }

    public void Apply(WorkspacePreferences workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        lock (_sync)
        {
            WorkspacePreferences normalized = Normalize(workspace).DeepCopy();
            _workspace = normalized;

            if (_currentTemplateFields.Count > 0)
            {
                _suppressTemplateFieldEvents = true;
                try
                {
                    foreach (WorkspaceTemplateFieldState field in _currentTemplateFields)
                    {
                        if (normalized.TemplateFieldValues.TryGetValue(field.Key, out string? value))
                        {
                            field.Value = value ?? string.Empty;
                        }
                    }
                }
                finally
                {
                    _suppressTemplateFieldEvents = false;
                }

                _workspace = _workspace with
                {
                    TemplateFieldValues = SnapshotTemplateFieldValuesLocked(),
                };
            }
        }
    }

    public void Persist()
    {
        lock (_sync)
        {
            if (!_initialized)
            {
                Initialize();
            }

            AppPreferences current = _store.Load();
            _store.Save(current with { Workspace = _workspace.DeepCopy() });
        }
    }

    private void ReplaceTemplateFieldsLocked(IEnumerable<WorkspaceTemplateFieldState> configured)
    {
        foreach (WorkspaceTemplateFieldState current in _currentTemplateFields)
        {
            current.PropertyChanged -= OnTemplateFieldChanged;
        }

        _currentTemplateFields.Clear();

        foreach (WorkspaceTemplateFieldState field in configured)
        {
            field.PropertyChanged += OnTemplateFieldChanged;
            _currentTemplateFields.Add(field);
        }
    }

    private Dictionary<string, string> SnapshotTemplateFieldValuesLocked()
    {
        Dictionary<string, string> snapshot = new(StringComparer.OrdinalIgnoreCase);

        foreach (WorkspaceTemplateFieldState field in _currentTemplateFields)
        {
            if (!string.IsNullOrWhiteSpace(field.Key))
            {
                snapshot[field.Key] = field.Value ?? string.Empty;
            }
        }

        return snapshot;
    }

    private void OnTemplateFieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkspaceTemplateFieldState.Value))
        {
            return;
        }

        bool changed = false;

        lock (_sync)
        {
            if (_suppressTemplateFieldEvents)
            {
                return;
            }

            _workspace = _workspace with
            {
                TemplateFieldValues = SnapshotTemplateFieldValuesLocked(),
            };
            changed = true;
        }

        if (changed)
        {
            OnWorkspaceChanged();
        }
    }


    private void OnWorkspaceChanged()
    {
        WorkspaceChanged?.Invoke(this, EventArgs.Empty);
    }

    private static WorkspacePreferences Normalize(WorkspacePreferences source)
    {
        WorkspacePreferences defaults = WorkspacePreferences.CreateDefault();

        string templateId = string.IsNullOrWhiteSpace(source.TemplateId)
            ? defaults.TemplateId
            : source.TemplateId.Trim();

        string tintKey = string.IsNullOrWhiteSpace(source.TintKey)
            ? defaults.TintKey
            : source.TintKey.Trim().ToLowerInvariant();

        string validityMode = ValidValidityModes.Contains(source.ValidityMode)
            ? source.ValidityMode
            : defaults.ValidityMode;

        string datePreset = ValidDatePresets.Contains(source.DatePreset)
            ? source.DatePreset
            : defaults.DatePreset;

        string expiryPreset = ValidDatePresets.Contains(source.ExpiryPreset)
            ? source.ExpiryPreset
            : defaults.ExpiryPreset;

        string dateDisplayFormat = ValidDateDisplayFormats.Contains(source.DateDisplayFormat)
            ? source.DateDisplayFormat
            : defaults.DateDisplayFormat;

        List<string> lines = source.CustomLines
            .Where(static x => x is not null)
            .Select(static x => x.Trim())
            .Take(5)
            .ToList();

        if (lines.Count == 0)
        {
            lines.Add(defaults.CustomLines[0]);
        }

        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in source.TemplateFieldValues)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                values[key.Trim()] = value ?? string.Empty;
            }
        }

        return source with
        {
            TemplateId = templateId,
            SelectedLineCount = Math.Clamp(source.SelectedLineCount, 1, 5),
            TemplateFieldValues = values,
            CustomLines = lines,
            TintKey = tintKey,
            Opacity = Math.Clamp(source.Opacity, 0.05, 0.85),
            FontSize = Math.Clamp(source.FontSize, 10, 72),
            HorizontalSpacing = Math.Clamp(source.HorizontalSpacing, 80, 700),
            VerticalSpacing = Math.Clamp(source.VerticalSpacing, 80, 700),
            AngleDegrees = Math.Clamp(source.AngleDegrees, 0, 360),
            ValidityMode = validityMode,
            DatePreset = datePreset,
            ExpiryPreset = expiryPreset,
            DateDisplayFormat = dateDisplayFormat,
            CustomDate = source.CustomDate,
            CustomExpiryDate = source.CustomExpiryDate,
        };
    }
}





