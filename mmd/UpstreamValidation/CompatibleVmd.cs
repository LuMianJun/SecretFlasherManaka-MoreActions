#if PRODUCTION_AVAILABLE
using System.Text;
using SFM.Mmd;

// Independent binary fixture rewrite: the original Player reads this file;
// the production adapter separately transforms the original parsed motion.
internal static class CompatibleVmd
{
    internal static void Write(string source,string destination)
    {
        byte[] bytes=File.ReadAllBytes(source);
        using var stream=new MemoryStream(bytes,true);using var reader=new BinaryReader(stream);
        var encoding=Encoding.GetEncoding(932);
        void Name(int length)
        {
            int offset=checked((int)stream.Position);byte[] original=reader.ReadBytes(length);
            string text=encoding.GetString(original).Split('\0')[0];
            // The expected-name membership comes from the untouched original
            // Reader, independently of production BoneName/Apply.
            string name=UnityVMDReader.VMDReader.BoneKeyFrameGroup.StringBoneNames.Contains(text)?text:MmdBoneMap.Normalize(text);
            byte[] normalized=encoding.GetBytes(name);
            if(normalized.Length>length)throw new InvalidDataException("Compatible name exceeds fixed field");
            Array.Clear(bytes,offset,length);Array.Copy(normalized,0,bytes,offset,normalized.Length);
        }
        stream.Position=50;uint bones=reader.ReadUInt32();
        for(uint i=0;i<bones;i++){Name(15);stream.Position+=96;}
        foreach(int stride in new[]{23,61,28,9})
        {
            if(stream.Position==stream.Length){File.WriteAllBytes(destination,bytes);return;}
            uint count=reader.ReadUInt32();stream.Position+=count*(long)stride;
        }
        if(stream.Position<stream.Length)
        {
            uint frames=reader.ReadUInt32();
            for(uint i=0;i<frames;i++)
            {
                stream.Position+=5;uint entries=reader.ReadUInt32();
                for(uint j=0;j<entries;j++){Name(20);stream.Position++;}
            }
        }
        File.WriteAllBytes(destination,bytes);
    }
    internal static void Verify(string folder,Action<bool,string> check)
    {
        string raw=OracleVmd.Write(Path.Combine(folder,"compat-raw.vmd"),rawKeys:true);
        string converted=Path.Combine(folder,"compat-expected.vmd");Write(raw,converted);
        var actual=SFM.Mmd.Upstream.UpstreamInputCompatibility.Apply(MmdMotion.Read(raw));
        var expected=new UnityVMDReader.VMDReader(converted);
        var reader=SFM.Mmd.Upstream.Reader.VMDReader.FromMotion(actual);
        check(actual.RawBoneKeys.Any(k=>k.Name=="左腕捩れ"),"Canonical wrist twist spelling retained");
        check(actual.RawBoneKeys.Any(k=>k.Name=="上半身2"),"Fullwidth upper-body alias accepted");
        check(actual.RawBoneKeys.Any(k=>k.Name=="左足ＩＫ"),"ASCII foot IK alias accepted");
        for(int i=0;i<expected.BoneKeyFrameGroups.Count;i++)
            check(expected.BoneKeyFrameGroups[i].BoneKeyFrames.Count==reader.BoneKeyFrameGroups[i].BoneKeyFrames.Count,"Independent compatible binary group count");
        int twist=UnityVMDReader.VMDReader.BoneKeyFrameGroup.StringBoneNames.IndexOf("左腕捩れ");
        check(reader.BoneKeyFrameGroups[twist].BoneKeyFrames.Count>0,"Canonical twist remains supported after compatibility");
        Console.WriteLine("UPSTREAM_INPUT_COMPATIBILITY PASS canonicalTwist=true aliases=true independentBinary=true");
    }
}
#endif
