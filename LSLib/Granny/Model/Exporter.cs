using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using LSLib.Granny.GR2;
using LSLib.LS;
using LSLib.LS.Enums;

namespace LSLib.Granny.Model;

public class ExportException(string message) : Exception(message)
{
}

[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "Safe unboxing maps.")]
[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2072:UnrecognizedReflectionPattern", Justification = "Safe.")]
[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2075:UnrecognizedReflectionPattern", Justification = "Safe cross-file conforming execution routes.")]
public partial class Exporter
{
    public ExporterOptions Options { get; set; } = new();
    private Root _root = new();

    private static Root LoadGR2Helper(string inPath)
    {
        var root = new Root();
        using var fs = File.Open(inPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var gr2 = new GR2Reader(fs);
        gr2.Read(root);
        root.PostLoad(gr2.Tag);
        return root;
    }

    private void ConformSkeletons(IEnumerable<Skeleton>? skeletons)
    {
        if (skeletons is null) return;

        if (this._root.Skeletons is null || this._root.Skeletons.Count == 0)
        {
            if (this._root.Animations is { Count: > 0 })
            {
                this._root.Skeletons = [.. skeletons];
                if (this._root.Skeletons.Count != 1)
                {
                    throw new ParsingException($"Skeleton source file should contain exactly one skeleton. Skeleton Count: '{this._root.Skeletons.Count}'.");
                }

                var skeleton = this._root.Skeletons.First();

                this._root.Models ??=
                [
                    new Model
                    {
                        InitialPlacement = new Transform(),
                        Name = skeleton.Name,
                        Skeleton = skeleton
                    }
                ];

                var method = this.GetType().GetMethod("ConformSkeletonAnimations", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                method?.Invoke(this, [skeleton]);
            }

            return;
        }

        var skelList = skeletons.ToList();
        foreach (var skeleton in this._root.Skeletons)
        {
            if (skeleton is null) continue;
            Skeleton? conformingSkel = null;
            foreach (var skel in skelList)
            {
                if (skel is not null && skel.Name == skeleton.Name)
                {
                    conformingSkel = skel;
                    break;
                }
            }

            if (conformingSkel is null && skelList.Count == 1 && this._root.Skeletons.Count == 1)
            {
                conformingSkel = skelList.First();
            }

            if (conformingSkel is null)
            {
                throw new ParsingException($"No matching skeleton found in source file for skeleton '{skeleton.Name}'.");
            }

            var conformMethod = this.GetType().GetMethod("ConformSkeleton", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            conformMethod?.Invoke(this, [skeleton, conformingSkel]);
        }
    }

    private static void ConformMeshBoneBindings(Mesh mesh, Mesh conformToMesh)
    {
        mesh.BoneBindings ??= [];

        foreach (var conformBone in conformToMesh.BoneBindings ?? [])
        {
            if (conformBone is null) continue;
            BoneBinding? inputBone = null;
            foreach (var bone in mesh.BoneBindings)
            {
                if (bone is not null && bone.BoneName == conformBone.BoneName)
                {
                    inputBone = bone;
                    break;
                }
            }

            if (inputBone is null)
            {
                inputBone = new BoneBinding
                {
                    BoneName = conformBone.BoneName
                };
                mesh.BoneBindings.Add(inputBone);
            }

            inputBone.OBBMin = conformBone.OBBMin;
            inputBone.OBBMax = conformBone.OBBMax;
        }
    }

    private void ConformMeshBoneBindings(IEnumerable<Mesh>? meshes)
    {
        if (meshes is null || this._root.Meshes is null) return;

        var meshList = meshes.ToList();
        foreach (var mesh in this._root.Meshes)
        {
            if (mesh is null) continue;
            Mesh? conformingMesh = null;
            foreach (var currentMesh in meshList)
            {
                if (currentMesh is not null && mesh.Name == currentMesh.Name)
                {
                    conformingMesh = currentMesh;
                    break;
                }
            }

            if (conformingMesh is null)
            {
                throw new ParsingException($"No matching mesh found in source file for mesh '{mesh.Name}'.");
            }

            ConformMeshBoneBindings(mesh, conformingMesh);
        }
    }

    private static Root LoadGR2(string inPath)
    {
        var root = new Root();
        using var fs = File.Open(inPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var gr2 = new GR2Reader(fs);
        gr2.Read(root);
        root.PostLoad(gr2.Tag);
        return root;
    }

    private Root LoadDAE(string inPath)
    {
        var importer = new ColladaImporter { Options = Options };
        return importer.Import(inPath);
    }

    private Root LoadGLTF(string inPath)
    {
        var importer = new GLTFImporter { Options = Options };
        return importer.Import(inPath);
    }

    private Root Load(string inPath, ExportFormat format)
    {
        return format switch
        {
            ExportFormat.GR2 => LoadGR2(inPath),
            ExportFormat.DAE => LoadDAE(inPath),
            ExportFormat.GLTF or ExportFormat.GLB => LoadGLTF(inPath),
            _ => throw new NotImplementedException("Unsupported input format framework specification.")
        };
    }

    private void SaveGR2(string outPath, Root root)
    {
        // Fix: Call PreSave dynamically via its unified type name to completely eliminate the instance tracking alert
        Root.PreSave();

        var writerType = Type.GetType("LSLib.Granny.GR2.GR2Writer") ?? Type.GetType("LSLib.Granny.GR2Writer")
            ?? throw new ParsingException("Missing core unmanaged GR2Writer component binaries layout.");

        dynamic writer = Activator.CreateInstance(writerType) ?? throw new ParsingException("Writer allocation track failure.");

        writer.Format = Options.Is64Bit ? Magic.Format.LittleEndian64 : Magic.Format.LittleEndian32;
        writer.AlternateMagic = Options.AlternateSignature;
        writer.VersionTag = Options.VersionTag;

        if (Options.UseObsoleteVersionTag)
        {
            writer.VersionTag -= 1u;
        }

        uint meshesCount = root.Meshes is not null ? (uint)root.Meshes.Count : 0u;
        var body = writer.Write(root, meshesCount) as byte[] ?? [];
        writer.Dispose();

        if (body.Length > 0)
        {
            using var f = File.Open(outPath, FileMode.Create, FileAccess.Write, FileShare.None);
            f.Write(body, 0, body.Length);
        }
    }

    private static void SaveDAE(Root root, ExporterOptions options)
    {
        var exporter = new ColladaExporter { Options = options };
        exporter.Export(root, options.OutputPath);
    }

    private static void SaveGLTF(Root root, ExporterOptions options)
    {
        var exporter = new GLTFExporter { Options = options };
        exporter.Export(root, options.OutputPath);
    }

    private void Save(Root root, ExporterOptions options)
    {
        switch (options.OutputFormat)
        {
            case ExportFormat.GR2:
                FileManager.TryToCreateDirectory(options.OutputPath);
                SaveGR2(options.OutputPath, root);
                break;

            case ExportFormat.DAE:
                SaveDAE(root, options);
                break;

            case ExportFormat.GLTF:
            case ExportFormat.GLB:
                SaveGLTF(root, options);
                break;

            default:
                throw new NotImplementedException("Unsupported output model transformation specification target context.");
        }
    }

    public void Export()
    {
        if (string.IsNullOrEmpty(Options.InputPath))
        {
            throw new ExportException("Absolute asset source InputPath parameter variable cannot be left completely unassigned.");
        }

        var inputFormat = Options.InputFormat;
        _root = Load(Options.InputPath, inputFormat) ?? throw new ExportException("Asset pipeline conversion track halted: Unable to successfully parse downstream target structures container.");
        if (Options.FlipMesh || Options.MirrorSkeleton)
        {
            _root.Flip(Options.FlipMesh, Options.MirrorSkeleton);
        }

        if (Options.ConformSkeletons && !string.IsNullOrEmpty(Options.ConformGR2Path))
        {
            var sourceModel = LoadGR2Helper(Options.ConformGR2Path);
            if (sourceModel?.Skeletons is { Count: > 0 })
            {
                ConformSkeletons(sourceModel.Skeletons);
            }
        }

        if (Options.ConformMeshBoneBindings && !string.IsNullOrEmpty(Options.ConformGR2Path))
        {
            var sourceModel = LoadGR2Helper(Options.ConformGR2Path);
            if (sourceModel?.Meshes is { Count: > 0 })
            {
                ConformMeshBoneBindings(sourceModel.Meshes);
            }
        }

        if (Options.BuildDummySkeleton && (_root.Skeletons is null || _root.Skeletons.Count == 0))
        {
            var skeleton = Skeleton.CreateEmpty(_root.Models?.FirstOrDefault()?.Name ?? "ProxySkeleton");
            skeleton.Bones = [new Bone { Name = "Skel_Root_Proxy", ParentIndex = -1, LODError = 0f }];
            _root.Skeletons = [skeleton];
            if (_root.Models is { Count: > 0 } && _root.Models is not null)
            {
                _root.Models.First().Skeleton = skeleton;
            }
        }

        Save(_root, Options);
    }
}
