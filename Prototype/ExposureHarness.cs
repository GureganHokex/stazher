using System;
using InGameSurface;
public static class ExposureHarness
{
    static void Check(bool value,string name) { if(!value)throw new Exception(name); }
    public static void Main()
    {
        var gate=new ExposureGate();int count=0;
        for(int i=0;i<50;i++)if(gate.Tick(true,.1))count++;
        Check(count==1,"continuous exposure must count exactly once");
        gate=new ExposureGate();for(int i=0;i<19;i++)gate.Tick(true,.1);
        gate.Tick(false,.1);for(int i=0;i<20;i++)Check(!gate.Tick(true,.1),"interrupted exposure counted early");
        Check(gate.Tick(true,.1),"second uninterrupted interval not counted");
        gate=new ExposureGate();for(int i=0;i<15;i++)gate.Tick(true,.1);
        Check(!gate.Tick(true,3) && gate.ContinuousSeconds==0,"stall accumulated visibility");
        gate.Interrupt();gate.Tick(true,.1);Check(gate.ContinuousSeconds==0,"focus loss accumulated time");
        gate=new ExposureGate(2,true);for(int i=0;i<50;i++)Check(!gate.Tick(true,.1),"reassigned creative double counted");
        Console.WriteLine("PASS: continuous time, interruption, deduplication, frame stall, focus interruption (5 scenarios)");
    }
}
