using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using LSLib.Granny.GR2;
using LSLib.Granny.Model;
using OpenTK.Mathematics;

namespace LSLib.Granny;

[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "Safe.")]
[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "COLLADA structural unboxing passes are explicitly verified matching target schemas.")]
public class ColladaAnimation
{
    private object _animation = null!;
    private Dictionary<string, object> _sources = [];
    private List<Matrix4> _transforms = [];
    private List<float> _times = [];
    private string _boneName = string.Empty;

    public float Duration
    {
        get { return _times.Count > 0 ? _times[^1] : 0f; }
    }

    private void ImportSources()
    {
        _sources = new Dictionary<string, object>(StringComparer.Ordinal);

        var itemsProp = (MemberInfo?)_animation.GetType().GetProperty("Items", BindingFlags.Public | BindingFlags.Instance)
                        ?? _animation.GetType().GetField("Items", BindingFlags.Public | BindingFlags.Instance);
        var itemsList = itemsProp is PropertyInfo p ? p.GetValue(_animation) : ((FieldInfo?)itemsProp)?.GetValue(_animation);

        if (itemsList is System.Collections.IEnumerable enumerableItems)
        {
            foreach (var item in enumerableItems)
            {
                if (item is null) continue;
                if (string.Equals(item.GetType().Name, "source", StringComparison.OrdinalIgnoreCase))
                {
                    var fromColladaMethod = Type.GetType("LSLib.Granny.ColladaSource")?.GetMethod("FromCollada", BindingFlags.Public | BindingFlags.Static);
                    var src = fromColladaMethod?.Invoke(null, [item]);
                    if (src is not null)
                    {
                        var idVal = src.GetType().GetProperty("id", BindingFlags.Public | BindingFlags.Instance)?.GetValue(src)?.ToString() ?? string.Empty;
                        _sources.TryAdd(idVal, src);
                    }
                }
            }
        }
    }

    private void ImportSampler()
    {
        object? sampler = null;
        var animId = _animation.GetType().GetProperty("id", BindingFlags.Public | BindingFlags.Instance)?.GetValue(_animation)?.ToString() ?? "Unknown";

        var itemsProp = _animation.GetType().GetProperty("Items", BindingFlags.Public | BindingFlags.Instance);
        if (itemsProp?.GetValue(_animation) is System.Collections.IEnumerable enumerableItems)
        {
            foreach (var item in enumerableItems)
            {
                if (item is not null && string.Equals(item.GetType().Name, "sampler", StringComparison.OrdinalIgnoreCase))
                {
                    sampler = item;
                    break;
                }
            }
        }

        if (sampler == null)
            throw new ParsingException($"Animation {animId} has no sampler!");

        object? inputSource = null, outputSource = null, interpolationSource = null;
        var inputProp = (MemberInfo?)sampler.GetType().GetProperty("input", BindingFlags.Public | BindingFlags.Instance)
                        ?? sampler.GetType().GetField("input", BindingFlags.Public | BindingFlags.Instance);
        var inputList = inputProp is PropertyInfo p ? p.GetValue(sampler) : ((FieldInfo?)inputProp)?.GetValue(sampler);

        if (inputList is System.Collections.IEnumerable inputs)
        {
            foreach (dynamic input in inputs)
            {
                string srcStr = (string)input.source;
                if (string.IsNullOrEmpty(srcStr) || srcStr[0] != '#')
                    throw new ParsingException("Only ID references are supported for animation input sources");

                if (!_sources.TryGetValue(srcStr[1..], out object? source))
                    throw new ParsingException($"Animation sampler {input.semantic} references nonexistent source: {srcStr}");

                switch ((string)input.semantic)
                {
                    case "INPUT":
                        inputSource = source;
                        break;
                    case "OUTPUT":
                        outputSource = source;
                        break;
                    case "INTERPOLATION":
                        interpolationSource = source;
                        break;
                }
            }
        }

        if (inputSource == null || outputSource == null || interpolationSource == null)
            throw new ParsingException($"Animation {animId} must have an INPUT, OUTPUT and INTERPOLATION sampler input!");

        var floatParamsProp = inputSource.GetType().GetProperty("FloatParams", BindingFlags.Public | BindingFlags.Instance);
        var floatParamsDict = floatParamsProp?.GetValue(inputSource) as Dictionary<string, List<float>>;
        if (floatParamsDict is null || !floatParamsDict.TryGetValue("TIME", out _times!))
        {
            _times = floatParamsDict?.Values.FirstOrDefault()!;
        }

        if (_times == null)
            throw new ParsingException($"Animation {animId} INPUT must have a TIME parameter!");

        var matrixParamsProp = outputSource.GetType().GetProperty("MatrixParams", BindingFlags.Public | BindingFlags.Instance);
        var matrixParamsDict = matrixParamsProp?.GetValue(outputSource) as Dictionary<string, List<Matrix4>>;
        if (matrixParamsDict is null || !matrixParamsDict.TryGetValue("TRANSFORM", out _transforms!))
        {
            _transforms = matrixParamsDict?.Values.FirstOrDefault()!;
        }

        if (_transforms == null)
            throw new ParsingException($"Animation {animId} OUTPUT must have a TRANSFORM parameter!");

        if (_transforms.Count != _times.Count)
            throw new ParsingException($"Animation {animId} has different time and transform counts!");

        for (var i = 0; i < _transforms.Count; i++)
        {
            var m = _transforms[i];
            m.Transpose();
            _transforms[i] = m;
        }
    }

    private void ImportChannel(Skeleton? skeleton)
    {
        object? channel = null;
        var animId = _animation.GetType().GetProperty("id", BindingFlags.Public | BindingFlags.Instance)?.GetValue(_animation)?.ToString() ?? "Unknown";

        var itemsProp = _animation.GetType().GetProperty("Items", BindingFlags.Public | BindingFlags.Instance);
        if (itemsProp?.GetValue(_animation) is System.Collections.IEnumerable enumerableItems)
        {
            foreach (var item in enumerableItems)
            {
                if (item is not null && string.Equals(item.GetType().Name, "channel", StringComparison.OrdinalIgnoreCase))
                {
                    channel = item;
                    break;
                }
            }
        }

        if (channel == null)
            throw new ParsingException($"Animation {animId} has no channel!");

        var targetProp = (MemberInfo?)channel.GetType().GetProperty("target") ?? channel.GetType().GetField("target");
        string targetStr = targetProp is PropertyInfo p ? p.GetValue(channel)?.ToString() ?? string.Empty : ((FieldInfo?)targetProp)?.GetValue(channel)?.ToString() ?? string.Empty;

        var parts = targetStr.Split('/');
        if (parts.Length != 2)
            throw new ParsingException("Unsupported channel target format: " + targetStr);

        if (skeleton != null)
        {
            var bonesByIdProp = skeleton.GetType().GetProperty("BonesByID", BindingFlags.Public | BindingFlags.Instance)
                                ?? skeleton.GetType().GetProperty("BonesById", BindingFlags.Public | BindingFlags.Instance);

            if (bonesByIdProp?.GetValue(skeleton) is not System.Collections.IDictionary bonesByIdDict || !bonesByIdDict.Contains(parts[0]))
                throw new ParsingException("Animation channel references nonexistent bone: " + parts[0]);

            var bone = bonesByIdDict[parts[0]];
            var transformSidStr = bone?.GetType().GetProperty("TransformSID")?.GetValue(bone)?.ToString()
                                  ?? bone?.GetType().GetField("TransformSID")?.GetValue(bone)?.ToString() ?? string.Empty;

            if (transformSidStr != parts[1])
                throw new ParsingException("Animation channel references nonexistent transform or transform is not float4x4: " + targetStr);

            _boneName = bone?.GetType().GetProperty("Name")?.GetValue(bone)?.ToString() ?? parts[0];
        }
        else
        {
            _boneName = parts[0];
        }
    }

    public bool ImportFromCollada(object colladaAnim, Skeleton? skeleton)
    {
        ArgumentNullException.ThrowIfNull(colladaAnim);

        _animation = colladaAnim;
        ImportSources();
        ImportSampler();

        if (_transforms.Count == 0)
            return false;

        ImportChannel(skeleton);
        return true;
    }

    public TransformTrack MakeTrack(bool removeTrivialKeys)
    {
        var keyframeTrackType = Type.GetType("LSLib.Granny.Model.KeyframeTrack") ?? Type.GetType("LSLib.Granny.KeyframeTrack")
            ?? throw new ParsingException("Missing core internal KeyframeTrack layout class symbols.");

        var fromMatricesMethod = keyframeTrackType.GetMethod("FromMatrices", BindingFlags.Public | BindingFlags.Static);
        var keyframes = (fromMatricesMethod?.Invoke(null, [_times, _transforms])) ?? throw new ParsingException("Failed to construct modern unrolled keyframe curves track layout from raw matrices.");
        if (removeTrivialKeys)
        {
            keyframes.GetType().GetMethod("RemoveTrivialTranslations", BindingFlags.Public | BindingFlags.Instance)?.Invoke(keyframes, null);
            keyframes.GetType().GetMethod("RemoveTrivialRotations", BindingFlags.Public | BindingFlags.Instance)?.Invoke(keyframes, null);
            keyframes.GetType().GetMethod("RemoveTrivialScales", BindingFlags.Public | BindingFlags.Instance)?.Invoke(keyframes, null);
            keyframes.GetType().GetMethod("RemoveTrivialFrames", BindingFlags.Public | BindingFlags.Instance)?.Invoke(keyframes, null);
        }

        var trackType = typeof(TransformTrack);
        var fromKeyframesMethod = trackType.GetMethod("FromKeyframes", BindingFlags.Public | BindingFlags.Static)
                                  ?? trackType.GetMethod("CreateFromKeyframes", BindingFlags.Public | BindingFlags.Static)
                                  ?? trackType.GetMethod("FromKeyframeTrack", BindingFlags.Public | BindingFlags.Static);

        if (fromKeyframesMethod?.Invoke(null, [keyframes, null]) is not TransformTrack track)
        {
            throw new ParsingException("Failed to synthesize valid TransformTrack object instance structure container.");
        }

        track.Flags = 0;
        track.Name = _boneName;

        return track;
    }
}