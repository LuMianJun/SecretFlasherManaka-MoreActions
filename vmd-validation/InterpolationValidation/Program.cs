using System.Text;
using Seleka2.Mmd;
using UnityEngine;

try
{
    int checks=0;
    void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
    void Near(double actual,double expected,string label,double tolerance=.000005)
        =>Check(Math.Abs(actual-expected)<=tolerance,$"{label}: actual={actual:G9}, expected={expected:G9}");

    // Curves chosen to have independent closed-form inverses. The expected
    // weights do not decode bytes or repeat the production bisection routine.
    double[] Weights(double x)
    {
        double t=Math.Cbrt(x);
        return new[]{x,Math.Pow(1-Math.Cbrt(1-x),3),1-Math.Pow(1-t,3),3*t*t-2*x};
    }
    byte[] Curves()
    {
        var bytes=Enumerable.Repeat((byte)73,64).ToArray();
        void Block(int start,byte x1,byte y1,byte x2,byte y2)
        {bytes[start]=x1;bytes[start+4]=y1;bytes[start+8]=x2;bytes[start+12]=y2;}
        Block(0,0,0,127,127);       // x=y: linear
        Block(16,127,0,127,0);     // x=1-(1-t)^3, y=t^3
        Block(32,0,127,0,127);     // x=t^3, y=1-(1-t)^3
        Block(48,0,0,0,127);       // x=t^3, y=3t^2-2t^3
        // Deliberately disagree with the channel blocks, as the reported VMD
        // does. Reading first-block interleaved bytes must fail these tests.
        foreach(int channel in new[]{1,2,3})
        {bytes[channel]=127;bytes[channel+4]=127;bytes[channel+8]=127;bytes[channel+12]=127;}
        return bytes;
    }
    var curves=Curves();
    foreach(double x in new[]{.05,.25,.5,.75,.95})
    {
        var expected=Weights(x);
        for(int channel=0;channel<4;channel++)Near(MmdMotion.Bezier(curves,channel,(float)x),expected[channel],$"Closed-form channel {channel} at {x}");
    }
    for(int channel=0;channel<4;channel++)
    {
        Near(MmdMotion.Bezier(curves,channel,0),0,$"Channel {channel} start");
        Near(MmdMotion.Bezier(curves,channel,1),1,$"Channel {channel} end");
    }
    Near(MmdMotion.Bezier(null,0,-1),0,"Before-start clamp still precedes control lookup");
    Near(MmdMotion.Bezier(null,3,2),1,"After-end clamp still precedes control lookup");

    var changedPadding=(byte[])curves.Clone();
    for(int i=0;i<64;i++)if(i%16!=0&&i%16!=4&&i%16!=8&&i%16!=12)changedPadding[i]=(byte)(255-i);
    for(int channel=0;channel<4;channel++)Near(MmdMotion.Bezier(changedPadding,channel,.25f),Weights(.25)[channel],$"Unused/redundant padding cannot alter channel {channel}");

    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    string directory=Path.Combine(Path.GetTempPath(),"mmd-interpolation-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string file=Path.Combine(directory,"fixture.vmd");
    try
    {
        using(var writer=new BinaryWriter(File.Create(file)))
        {
            void Text(string text,int length)
            {var value=new byte[length];Encoding.GetEncoding(932).GetBytes(text).CopyTo(value,0);writer.Write(value);}
            Text("Vocaloid Motion Data 0002",30);Text("synthetic",20);writer.Write(2u);
            foreach(uint frame in new[]{0u,30u})
            {
                Text("上半身",15);writer.Write(frame);
                // Raw X/Z are inverted by the existing coordinate conversion.
                writer.Write(frame==0?-2f:-12f);writer.Write(frame==0?4f:24f);writer.Write(frame==0?-8f:-48f);
                var q=Quaternion.AngleAxis(frame==0?30:150,Vector3.up);
                writer.Write(q.x);writer.Write(q.y);writer.Write(q.z);writer.Write(q.w);
                // The source key intentionally has different curves. Sample
                // must continue using the destination key's interpolation.
                writer.Write(frame==0?Enumerable.Repeat((byte)127,64).ToArray():curves);
            }
            writer.Write(0u); // No morphs or external media.
        }
        var motion=MmdMotion.Read(file);
        foreach(float fraction in new[]{.25f,.5f,.75f})
        {
            var sample=motion.Sample("上半身",fraction*30);
            var expected=Weights(fraction);
            Near(sample.p.x,2+10*expected[0],$"Sample X at {fraction}",.0001);
            Near(sample.p.y,4+20*expected[1],$"Sample Y at {fraction}",.0001);
            Near(sample.p.z,8+40*expected[2],$"Sample Z at {fraction}",.0001);
            var expectedQ=Quaternion.AngleAxis((float)(30+120*expected[3]),Vector3.up);
            Check(Quaternion.Angle(sample.q,expectedQ)<.1,$"Sample rotation uses its own channel at {fraction}");
        }
        foreach(float frame in new[]{-10f,0f,30f,40f})
        {
            bool first=frame<=0;var sample=motion.Sample("上半身",frame);
            Near(sample.p.x,first?2:12,$"Endpoint X at {frame}");
            Near(sample.p.y,first?4:24,$"Endpoint Y at {frame}");
            Near(sample.p.z,first?8:48,$"Endpoint Z at {frame}");
            Check(Quaternion.Angle(sample.q,Quaternion.AngleAxis(first?30:150,Vector3.up))<.1,$"Endpoint rotation at {frame}");
        }
        var missing=motion.Sample("missing",15);
        Check(missing.p.sqrMagnitude==0&&Quaternion.Angle(missing.q,Quaternion.identity)<.1,"Missing-track semantics remain neutral");
    }
    finally
    {
        if(File.Exists(file))File.Delete(file);
        Directory.Delete(directory); // Only this empty, uniquely created directory.
    }
    Console.WriteLine($"PASS {checks} assertions: four independent bone curves, contradictory redundant data, padding, parsed VMD Sample positions/rotation and endpoint behavior");
}
catch(Exception error)
{
    Console.Error.WriteLine(error);Environment.ExitCode=1;
}
