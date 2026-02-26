using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Syroot.BinaryData;

namespace NBSDecoder;

public class NewBasis
{
    // This is some kind of animation format that houses vp9 frames for each keyframe.

    // They have 2 VP9 decoders:
    // - NBSDecoderInner, google's vp9 library; usually statically linked into their game
    // - NBSDecoderOuter, ffmpeg as a side dynamic library with vp9 support only (libnbsextend.so or nbsextend.dll)
    //
    // They made some changes to vp9 in BOTH cases that makes the original library or ffmpeg incompatible.
    //
    // Namely:
    // - there is no frame header magic (0b10), so first byte is always 0x32 instead of i.e 0xA2
    // - read_bitdepth_colorspace_sampling doesn't seem to read the extra 3 bits for subsampling_x/subsampling_y either.
    //   essentially, after sync code, and color_space read, there is no 3 bit read (1 (subsampling_x) + 1 (subsampling_x) + 1 (reserved))
    // - (not relevant to video frame format) some way to batch (?) serialize frames, with new vpx_codec_control values (270+)
    //   - 270 = set serial frame
    //   - 273 = serial frame allocator
    //   - 274 = serial frame deallocator
    // - decoder eventually calls SerialFrameBuffers, which calls vpx::SerialerMgr::GetSerialer.
    // 
    // - ... and more to figure out.
    //
    // For now we use theirs.
    // It would be wise to maybe diff nbsextend.dll with a own compiled ffmpeg, since the compile arguments are included in the executable.
    // Would make diffing easier.


    public byte UnkType { get; set; }

    public List<ClipInfo> Clips { get; set; } = [];
    public List<KeyData> KeyBaseData { get; set; } = [];
    public List<KeyData> KeyAlphaData { get; set; } = [];

    public byte ClipCount { get; set; }
    public byte Field_0x01 { get; set; }
    public uint Width { get; set; }
    public uint Height { get; set; }
    public uint FrameCount { get; set; }
    public uint FrameRate { get; set; }
    public ulong BaseColorDataVP9StartOffset { get; set; }
    public ulong BaseColorDataVP9Size { get; set; }
    public string[] ClipNames { get; set; }

    public static NewBasis Open(string file)
    {
        var nbs = new NewBasis();

        using var fs = File.OpenRead(file);
        nbs.OpenImpl(fs);
        return nbs;
    }

    public void DumpInfo()
    {
        Console.WriteLine($"> Clip Count: {ClipCount}");
        Console.WriteLine($"> Dimensions: {Width}x{Height}");
        Console.WriteLine($"> Frames: {FrameCount}");
        Console.WriteLine($"> Frame Rate: {FrameRate}");
        Console.WriteLine($"> Base Color Data Offset: 0x{BaseColorDataVP9StartOffset:X}");
        Console.WriteLine($"> Base Color Data Offset: 0x{BaseColorDataVP9Size:X}");

        for (int i = 0; i < Clips.Count; i++)
        {
            ClipInfo clipInfo = Clips[i];
            Console.WriteLine($"> Clip '{ClipNames[i]}' [{clipInfo.KeyStart}->{clipInfo.KeyStart+clipInfo.NumKeys-1}]");
        }
    }

    private void OpenImpl(Stream stream)
    {
        BinaryStream bs = new BinaryStream(stream);
        byte version = bs.Read1Byte();
        byte unkType = bs.Read1Byte();

        Console.WriteLine($"Version: {version}");
        Console.WriteLine($"Type: {unkType}");

        byte numSections = bs.Read1Byte();
        Console.WriteLine($"Num sections: {numSections}");

        List<NbsSection> sections = new(numSections);
        for (int i = 0; i < numSections; i++)
            sections.Add(new NbsSection((NbsSectionType)bs.Read1Byte(), bs.ReadInt32()));

        foreach (var section in sections)
        {
            bs.Position = section.Offset;
            switch (section.Type)
            {
                case NbsSectionType.BaseInfo:
                    ReadBaseInfo(bs);
                    break;
                case NbsSectionType.KeyInfo:
                    ReadKeys(bs);
                    break;
                case NbsSectionType.BaseColorData:
                    Console.WriteLine("TODO: BaseColorData section");
                    break;
                case NbsSectionType.AudioInfo:
                    Console.WriteLine("TODO: AudioInfo section");
                    break;
                case NbsSectionType.AlphaInfo:
                    ReadAlphaInfo(bs);
                    break;
                case NbsSectionType.KeyInfo6:
                    Console.WriteLine("TODO: KeyInfo6 section");
                    break;
                case NbsSectionType.AlphaInfo8:
                    Console.WriteLine("TODO: AlphaInfo8 section");
                    break;
                default:
                    Console.WriteLine($"WARN: Unsupported section type {section.Type}, skipping");
                    break;
            }
        }

    }

    private void ReadBaseInfo(BinaryStream bs)
    {
        ClipCount = bs.Read1Byte();
        Field_0x01 = bs.Read1Byte();
        Width = bs.ReadUInt32();
        Height = bs.ReadUInt32();
        FrameCount = bs.ReadUInt32();
        FrameRate = bs.ReadUInt32();
        BaseColorDataVP9StartOffset = bs.ReadUInt32();
        BaseColorDataVP9Size = bs.ReadUInt32();

        uint namesLength = bs.ReadUInt32();
        byte[] nameBuffer = bs.ReadBytes((int)namesLength);
        string str = Encoding.UTF8.GetString(nameBuffer);
        ClipNames = str.Split(" ");

        for (int i = 0; i < ClipCount; i++)
        {
            var clip = new ClipInfo()
            {
                KeyStart = bs.ReadUInt32(),
                NumKeys = bs.ReadUInt32(),
                BaseColorDataOffset = bs.ReadUInt32(),
                BaseColorDataSize = bs.ReadUInt32(),
            };
            Clips.Add(clip);
        }
    }

    private void ReadAlphaInfo(BinaryStream bs)
    {
        byte version = bs.Read1Byte();
        if (version == 1)
        {
            sbyte unk = bs.ReadSByte();
            return;
        }

        // ClipAlphaData
        for (int i = 0; i < ClipCount; i++)
        {
            uint dataStart = bs.ReadUInt32();
            uint dataEnd = bs.ReadUInt32();
        }

        long basePos = bs.Position;
        for (int i = 0; i < KeyBaseData.Count; i++)
        {
            bs.Position = basePos + (i * 0x08);

            uint keyIndex = bs.ReadUInt32();
            int frameDataOffset = bs.ReadInt32();

            bs.Position = frameDataOffset;
            int frameDataSize = bs.ReadInt32();
            byte[] frameData = bs.ReadBytes(frameDataSize);

            KeyAlphaData.Add(new KeyData(keyIndex, frameData));
        }
    }
    private void ReadKeys(BinaryStream bs)
    {
        int numKeys = bs.ReadInt32();

        long basePos = bs.Position;
        for (int i = 0; i < numKeys; i++)
        {
            bs.Position = basePos + (i * 0x08);

            uint keyIndex = bs.ReadUInt32();
            int frameDataOffset = bs.ReadInt32();

            bs.Position = frameDataOffset;
            int frameDataSize = bs.ReadInt32();
            byte[] frameData = bs.ReadBytes(frameDataSize);

            KeyBaseData.Add(new KeyData(keyIndex, frameData));
        }
    }
}

public class ClipInfo
{
    public uint KeyStart { get; set; }
    public uint NumKeys { get; set; }
    public uint BaseColorDataOffset { get; set; }
    public uint BaseColorDataSize { get; set; }
}

public record NbsSection(NbsSectionType Type, int Offset);
public record KeyData(uint KeyIndex, byte[] Data);

public enum NbsSectionType : byte
{
    BaseInfo = 0,
    KeyInfo = 1,
    BaseColorData = 2, // Data read by nbs::BaseColorDecoder (inherits from nbs::VP9Decoder)
    AudioInfo = 3,
    AlphaInfo = 4, // Data read by nbs::AlphaDecoder (inherits from nbs::VP9Decoder)
    KeyInfo6 = 6,
    AlphaInfo8 = 8,
}

