using Unity.Netcode.Components;

// Lets the owning client drive its own position instead of the server,
// which is what a CharacterController-driven first-person player needs.
public class ClientNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}
