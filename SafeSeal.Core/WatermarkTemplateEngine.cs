using System.Globalization;
using System.Text.RegularExpressions;

namespace SafeSeal.Core;

public sealed class WatermarkTemplateEngine
{
    private static readonly Regex TokenRegex = new("\\{\\{\\s*([a-zA-Z0-9_.-]+)\\s*\\}\\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Render(TemplateDefinition2 definition, IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);

        List<string> errors = Validate(definition, values);
        if (errors.Count > 0)
        {
            throw new TemplateValidationException(errors);
        }

        string rendered = TokenRegex.Replace(definition.Content, match =>
        {
            string key = match.Groups[1].Value;
            string? value = values.TryGetValue(key, out string? provided)
                ? provided
                : definition.Variables.FirstOrDefault(v => string.Equals(v.Key, key, StringComparison.Ordinal))?.DefaultValue;

            return (value ?? string.Empty).Trim();
        });

        return rendered;
    }

    public List<string> Validate(TemplateDefinition2 definition, IReadOnlyDictionary<string, string?> values)
    {
        List<string> errors = new();

        foreach (TemplateVariableDefinition variable in definition.Variables)
        {
            values.TryGetValue(variable.Key, out string? raw);
            string text = string.IsNullOrWhiteSpace(raw) ? (variable.DefaultValue ?? string.Empty) : raw.Trim();

            if (variable.Required && string.IsNullOrWhiteSpace(text))
            {
                errors.Add($"{variable.Key} is required.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            switch (variable.ValueType)
            {
                case TemplateValueType.String:
                    ValidateString(variable, text, errors);
                    break;
                case TemplateValueType.Number:
                    ValidateNumber(variable, text, errors);
                    break;
                case TemplateValueType.Date:
                    ValidateDate(variable, text, errors);
                    break;
                case TemplateValueType.Bool:
                    ValidateBool(variable, text, errors);
                    break;
                case TemplateValueType.Enum:
                    ValidateEnum(variable, text, errors);
                    break;
            }
        }

        return errors;
    }

    private static void ValidateString(TemplateVariableDefinition variable, string value, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(variable.RegexPattern)
            && !Regex.IsMatch(value, variable.RegexPattern, RegexOptions.CultureInvariant))
        {
            errors.Add($"{variable.Key} does not match required format.");
        }

        if (variable.Min.HasValue && value.Length < variable.Min.Value)
        {
            errors.Add($"{variable.Key} is shorter than minimum length {variable.Min.Value}.");
        }

        if (variable.Max.HasValue && value.Length > variable.Max.Value)
        {
            errors.Add($"{variable.Key} exceeds maximum length {variable.Max.Value}.");
        }
    }

    private static void ValidateNumber(TemplateVariableDefinition variable, string value, List<string> errors)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            errors.Add($"{variable.Key} must be a number.");
            return;
        }

        if (variable.Min.HasValue && parsed < variable.Min.Value)
        {
            errors.Add($"{variable.Key} is smaller than {variable.Min.Value}.");
        }

        if (variable.Max.HasValue && parsed > variable.Max.Value)
        {
            errors.Add($"{variable.Key} is greater than {variable.Max.Value}.");
        }
    }

    private static void ValidateDate(TemplateVariableDefinition variable, string value, List<string> errors)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out _)
            && !DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out _))
        {
            errors.Add($"{variable.Key} must be a valid date.");
        }
    }

    private static void ValidateBool(TemplateVariableDefinition variable, string value, List<string> errors)
    {
        if (!bool.TryParse(value, out _))
        {
            errors.Add($"{variable.Key} must be true or false.");
        }
    }

    private static void ValidateEnum(TemplateVariableDefinition variable, string value, List<string> errors)
    {
        if (variable.EnumValues is null || variable.EnumValues.Count == 0)
        {
            return;
        }

        bool match = variable.EnumValues.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
        if (!match)
        {
            errors.Add($"{variable.Key} is not one of the allowed values.");
        }
    }
}
