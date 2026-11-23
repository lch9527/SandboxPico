using System;
using UnityEngine;

namespace PhysicalAssembly
{
    public enum AssemblyInteractionKind { Grab, Attach, Detach, Reject }
    public sealed partial class AssemblyWorld
    {
        Pose InteractionPose(ConnectionRecord connection)
        {
            var body=Resolve(connection.b); var socket=body?body.Part.FindSocket(connection.socketB):null;
            return socket?new Pose(socket.WorldPosition,socket.WorldRotation):body?new Pose(body.transform.TransformPoint(connection.anchorB),body.transform.rotation):default;
        }
        // User actions only: raw Connect/RemoveConnection used by saves and resets remain silent.
        public event Action<AssemblyInteractionKind, AssemblyPart, Vector3> Interaction;
        public event Action<AssemblyInteractionKind, AssemblyPart, Vector3, Vector3> InteractionPresented;
        void NotifyInteraction(AssemblyInteractionKind kind, AssemblyPart part, Vector3 point, Vector3 normal=default)
        { Interaction?.Invoke(kind, part, point); InteractionPresented?.Invoke(kind,part,point,normal); }
    }
}
