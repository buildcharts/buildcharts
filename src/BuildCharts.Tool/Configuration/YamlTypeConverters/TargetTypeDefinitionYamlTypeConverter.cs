using BuildCharts.Tool.Configuration.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace BuildCharts.Tool.Configuration.YamlTypeConverters;

/// <summary>
/// Allows for flexible types with an array.
///
///  krp.sln: build
///
///  krp.sln:
///    type: [build, nuget]
///    with: ...
///
///  krp.sln:
///    - type: build
///      with: ...
///    - type: nuget
///      with: ...
/// </summary>
public class TargetTypeDefinitionYamlTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(List<TargetDefinition>);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer deserialize)
    {
        // Case 1: Sequence (e.g., list of full objects or scalars)
        if (parser.Accept<SequenceStart>(out _))
        {
            parser.TryConsume<SequenceStart>(out _);

            var list = new List<TargetDefinition>();

            while (!parser.Accept<SequenceEnd>(out _))
            {
                if (parser.Accept<Scalar>(out var scalar))
                {
                    parser.TryConsume(out scalar);
                    list.Add(new TargetDefinition
                    {
                        Type = scalar.Value,
                        With = new Dictionary<string, object>(),
                    });
                }
                else
                {
                    var item = (TargetDefinition)deserialize(typeof(TargetDefinition));
                    list.Add(item);
                }
            }

            parser.TryConsume<SequenceEnd>(out _);
            return list;
        }

        // Case 2: Mapping
        if (parser.Accept<MappingStart>(out _))
        {
            // Do not consume mapping yet, let deserializer handle it cleanly
            var raw = deserialize(typeof(Dictionary<string, object>)) as Dictionary<string, object>;
            if (raw != null && raw.TryGetValue("type", out var typeValue))
            {
                var types = typeValue switch
                {
                    string s => new List<string> { s },
                    IEnumerable<object> list => list.OfType<string>().ToList(),
                    _ => throw new YamlException("Invalid 'type' format; expected string or list of strings")
                };

                var with = raw.TryGetValue("with", out var withVal) && withVal is Dictionary<object, object> dict
                    ? dict.ToDictionary(k => k.Key.ToString(), v => v.Value)
                    : new Dictionary<string, object>();

                var items = types.Select(t => new TargetDefinition
                {
                    Type = t,
                    With = new Dictionary<string, object>(with),
                }).ToList();

                return items;
            }
            else
            {
                throw new YamlException("Target definition mapping must include a 'type' value.");
            }
        }

        // Case 3: Scalar string (e.g., krp.sln: build)
        if (parser.TryConsume<Scalar>(out var scalarValue))
        {
            var def = new TargetDefinition
            {
                Type = scalarValue.Value,
                With = new Dictionary<string, object>()
            };
            return new List<TargetDefinition> { def };
        }

        throw new YamlException("Unsupported YAML format for target definitions.");
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        serializer(value);
    }
}

