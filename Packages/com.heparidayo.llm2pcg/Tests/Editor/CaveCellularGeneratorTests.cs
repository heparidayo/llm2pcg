using System.Collections.Generic;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Generators.Cave;
using NUnit.Framework;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class CaveCellularGeneratorTests
    {
        [Test] public void SameRequest_ProducesSameHash() { CaveCellularGenerator g = new CaveCellularGenerator(); PCGRequest r = PCGRequest.CreateCaveDefault(811); Assert.That(g.GenerateCave(r).ComputeStableHash(), Is.EqualTo(g.GenerateCave(r).ComputeStableHash())); }
        [Test] public void DifferentSeed_ProducesDifferentHash() { CaveCellularGenerator g = new CaveCellularGenerator(); Assert.That(g.GenerateCave(PCGRequest.CreateCaveDefault(811)).ComputeStableHash(), Is.Not.EqualTo(g.GenerateCave(PCGRequest.CreateCaveDefault(812)).ComputeStableHash())); }
        [Test] public void BoundaryCells_AreSolid() { CaveWorldData world = new CaveCellularGenerator().GenerateCave(PCGRequest.CreateCaveDefault(34)); for (int x=0;x<world.Width;x++) { Assert.That(world.SolidCells[x]); Assert.That(world.SolidCells[(world.Height-1)*world.Width+x]); } }
        [Test] public void StartAndExit_AreConnected() { CaveWorldData world = new CaveCellularGenerator().GenerateCave(PCGRequest.CreateCaveDefault(77)); Assert.That(Reachable(world, world.StartPosition, world.ExitPosition), Is.True); }
        [Test] public void AllOpenCells_AreConnectedAfterTunnels() { CaveWorldData world = new CaveCellularGenerator().GenerateCave(PCGRequest.CreateCaveDefault(221)); int total=0, reached=0; foreach(bool solid in world.SolidCells) if(!solid) total++; Queue<Int2> q=new Queue<Int2>(); bool[] seen=new bool[world.SolidCells.Length];q.Enqueue(world.StartPosition);seen[world.StartPosition.Y*world.Width+world.StartPosition.X]=true;while(q.Count>0){Int2 p=q.Dequeue();reached++; Add(world,p.X,p.Y-1,seen,q);Add(world,p.X+1,p.Y,seen,q);Add(world,p.X,p.Y+1,seen,q);Add(world,p.X-1,p.Y,seen,q);} Assert.That(reached,Is.EqualTo(total)); }
        private static bool Reachable(CaveWorldData world, Int2 start, Int2 end) { Queue<Int2> q=new Queue<Int2>();bool[] seen=new bool[world.SolidCells.Length];q.Enqueue(start);seen[start.Y*world.Width+start.X]=true;while(q.Count>0){Int2 p=q.Dequeue();if(p.X==end.X&&p.Y==end.Y)return true;Add(world,p.X,p.Y-1,seen,q);Add(world,p.X+1,p.Y,seen,q);Add(world,p.X,p.Y+1,seen,q);Add(world,p.X-1,p.Y,seen,q);}return false; }
        private static void Add(CaveWorldData w,int x,int y,bool[] seen,Queue<Int2> q){if(w.IsWalkable(x,y)&&!seen[y*w.Width+x]){seen[y*w.Width+x]=true;q.Enqueue(new Int2(x,y));}}
    }
}
