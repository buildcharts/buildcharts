using System;
using System.Collections.Generic;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace BuildCharts.Tool.Configuration.YamlTypeConverters;

/// <summary>
/// Allows variables to be defined either as:
/// variables:
///   - VERSION
///   - COMMIT
/// or as:
/// variables:
///   VERSION: "1.0.0"
///   COMMIT: ""
/// </summary>
public class BuildVariablesYamlTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(Dictionary<string, string>);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer deserialize)
    {
        // Sequence -> list of names, no defaults
        if (parser.Accept<SequenceStart>(out _))
        {
            parser.Consume<SequenceStart>();
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (!parser.Accept<SequenceEnd>(out _))
            {
                if (parser.TryConsume<Scalar>(out var scalar))
                {
                    var text = scalar.Value ?? string.Empty;
                    var eqIdx = text.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        var key = text[..eqIdx];
                        var value = eqIdx < text.Length - 1 ? text[(eqIdx + 1)..] : string.Empty;
                        dict[key] = value;
                    }
                    else
                    {
                        dict[text] = null;
                    }
                }
                else
                {
                    throw new YamlException("Variables sequence must contain only scalars");
                }
            }

            parser.Consume<SequenceEnd>();
            return dict;
        }

        // Mapping -> name: default
        if (parser.Accept<MappingStart>(out _))
        {
            var raw = deserialize(typeof(Dictionary<string, object>)) as Dictionary<string, object>;
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (raw != null)
            {
                foreach (var kv in raw)
                {
                    dict[kv.Key] = kv.Value?.ToString();
                }
            }
            return dict;
        }

        throw new YamlException("Variables must be a sequence or mapping");
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        serializer(value);
    }
}
