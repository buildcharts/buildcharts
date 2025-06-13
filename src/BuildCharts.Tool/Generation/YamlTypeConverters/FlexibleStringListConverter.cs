using BuildCharts.Tool.Generation.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace BuildCharts.Tool.Generation.YamlTypeConverters;

/// <summary>
/// Allows for flexible types with an array 
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
/// <typeparam name="T"></typeparam>
public class FlexibleListYamlTypeConverter<T> : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(FlexibleList<T>);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer deserialize)
    {
        // Case 1: Sequence (e.g., list of full objects or scalars)
        if (parser.Accept<SequenceStart>(out _))
        {
            parser.TryConsume<SequenceStart>(out _);

            var list = new List<T>();

            while (!parser.Accept<SequenceEnd>(out _))
            {
                if (parser.Accept<Scalar>(out var scalar))
                {
                    parser.TryConsume<Scalar>(out scalar);
                    var def = new TargetDefinition
                    {
                        Type = scalar.Value,
                        With = new Dictionary<string, object>()
                    };
                    list.Add((T)(object)def);
                }
                else
                {
                    var item = (T)deserialize(typeof(T));
                    list.Add(item);
                }
            }

            parser.TryConsume<SequenceEnd>(out _);
            return new FlexibleList<T>(list);
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
                }).Cast<T>().ToList();

                return new FlexibleList<T>(items);
            }
            else
            {
                // No "type" key, treat mapping as single ArtifactDefinition
                var single = (T)deserialize(typeof(T));
                return new FlexibleList<T>(new List<T> { single });
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
            return new FlexibleList<T>(new List<T> { (T)(object)def });
        }

        throw new YamlException("Unsupported YAML format for FlexibleList<T>");
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        var list = (FlexibleList<T>)value;
        serializer(list);
    }
}

public class FlexibleList<T> : List<T>
{
    public FlexibleList()
    {
    }

    public FlexibleList(IEnumerable<T> items)
        : base(items)
    {
    }
}