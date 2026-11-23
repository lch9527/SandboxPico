using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests
{
    public sealed partial class SandboxAttachmentTests
    {
        // A carried group connects through any of its parts, not only the one in the hand.
        [UnityTest] public IEnumerator GroupConnectsThroughPartThatIsNotHeld()
        {
            var lower=Part(Vector3.up*4); var upper=Part(Vector3.up*5);
            Assert.IsTrue(World.TryBeginAssembly(Prepare(upper,lower,Vector3.up,0,0))); yield return Finish();
            CollectionAssert.Contains(World.GetConnectedParts(upper.InstanceId),lower);

            // Bring the lower part's free socket next to the core; the upper part's sockets stay out of range.
            var before=lower.MainBody.Rigidbody.position;
            Prepare(lower,Level.Core,Vector3.up,0,.01f);
            var shift=lower.MainBody.Rigidbody.position-before;
            foreach(var body in upper.Bodies) { body.Rigidbody.position+=shift; body.transform.position=body.Rigidbody.position; }
            Physics.SyncTransforms();
            Assert.IsFalse(upper.Sockets.Any(s=>!World.SocketOccupied(s) && Level.Core.Sockets.Any(t=>Vector3.Distance(s.WorldPosition,t.WorldPosition)<=World.SocketDistance)),
                "Fixture: the queried part must have no socket of its own in range.");

            var candidate=World.Query(upper);
            Assert.IsTrue(candidate.IsValid,"The group's other part is in range of the core.");
            Assert.AreEqual(lower,candidate.source.Part); Assert.AreEqual(Level.Core,candidate.target.Part);
            Assert.IsTrue(World.TryBeginAssembly(candidate)); yield return Finish();
            CollectionAssert.Contains(World.GetConnectedParts(Level.Core.InstanceId),upper);
        }
    }
}
