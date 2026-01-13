using BuildCharts.Tool.Configuration.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace BuildCharts.Tool.Configuration.YamlTypeConverters;

/// <summary>
/// Allows for flexible variables with sequences.
///
///  variables:
///    - VERSION
///    - COMMIT
///
///  variables:
///    - VERSION: "1.0.0"
///    - COMMIT: ""
///
///  variables:
///    - VERSION:
///        default: "1.0.0"
///    - COMMIT: ""
///
///  variables:
///    VERSION: "1.0.0"
///    COMMIT: ""
/// </summary>
public class VariableDefinitionsYamlTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(Dictionary<string, VariableDefinition>);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer deserialize)
    {
        // Case 1: Sequence (list of scalars or mappings).
        if (parser.Accept<SequenceStart>(out _))
        {
            parser.Consume<SequenceStart>();
            var variables = new Dictionary<string, VariableDefinition>(StringComparer.OrdinalIgnoreCase);

            while (!parser.Accept<SequenceEnd>(out _))
            {
                // Case 1a: Scalar (NAME only).
                if (parser.TryConsume<Scalar>(out var scalar))
                {
                    var text = scalar.Value ?? string.Empty;
                    if (text.Contains('='))
                    {
                        throw new YamlException("Variable definition scalar must not contain '='.");
                    }

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        throw new YamlException("Variable definition must have a non-empty name.");
                    }

                    variables[text] = new VariableDefinition { Default = string.Empty };
                    continue;
                }

                // Case 1b: Mapping (NAME: value or NAME: { default: value }).
                if (parser.Accept<MappingStart>(out _))
                {
                    if (deserialize(typeof(Dictionary<object, object>)) is not Dictionary<object, object> raw || raw.Count != 1)
                    {
                        throw new YamlException("Variable definition must contain exactly one entry.");
                    }

                    var entry = raw.First();
                    var name = entry.Key?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        throw new YamlException("Variable definition must have a non-empty name.");
                    }

                    if (entry.Value is Dictionary<object, object> valueMap)
                    {
                        if (!valueMap.TryGetValue("default", out var defaultValue))
                        {
                            throw new YamlException("Variable definition mapping must include a 'default' value.");
                        }

                        variables[name] = new VariableDefinition
                        {
                            Default = defaultValue?.ToString() ?? string.Empty,
                        };
                    }
                    else
                    {
                        variables[name] = new VariableDefinition
                        {
                            Default = entry.Value?.ToString() ?? string.Empty,
                        };
                    }

                    continue;
                }

                throw new YamlException("Variables sequence items must be scalars or mappings.");
            }

            parser.Consume<SequenceEnd>();
            return variables;
        }

        // Case 2: Mapping (NAME: value).
        if (parser.Accept<MappingStart>(out _))
        {
            if (deserialize(typeof(Dictionary<object, object>)) is not Dictionary<object, object> raw)
            {
                throw new YamlException("Variables mapping must contain entries.");
            }

            var variables = new Dictionary<string, VariableDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in raw)
            {
                var name = entry.Key?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new YamlException("Variable definition must have a non-empty name.");
                }

                variables[name] = new VariableDefinition
                {
                    Default = entry.Value?.ToString() ?? string.Empty,
                };
            }

            return variables;
        }

        throw new YamlException("Variables must be a sequence or mapping.");
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        serializer(value);
    }
}
