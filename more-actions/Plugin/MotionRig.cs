namespace Seleka2.Actions;
using System.Collections.Generic;

public static class MotionRig
{
    public static IEnumerable<string> BonePaths()
    {
        const string hips="Armature/Hips";
        const string chest=hips+"/Spine/Chest";
        yield return hips;yield return hips+"/Spine";yield return chest;
        yield return chest+"/Neck";yield return chest+"/Neck/Head";
        foreach(var side in new[]{"_L","_R"})
        {
            var shoulder=chest+"/Shoulder"+side;
            var upperArm=shoulder+"/UpperArm"+side;
            var lowerArm=upperArm+"/LowerArm"+side;
            var hand=lowerArm+"/Hand"+side;
            yield return shoulder;yield return upperArm;yield return lowerArm;yield return hand;
            var upperLeg=hips+"/UpperLeg"+side;var lowerLeg=upperLeg+"/LowerLeg"+side;
            yield return upperLeg;yield return lowerLeg;yield return lowerLeg+"/Foot"+side;
            foreach(var finger in new[]{"Thumb","Index","Middle","Ring","Little"})
            {
                var proximal=hand+"/"+finger+"Proximal"+side;
                var intermediate=proximal+"/"+finger+"Intermediate"+side;
                yield return proximal;yield return intermediate;yield return intermediate+"/"+finger+"Distal"+side;
            }
        }
    }
}
