/// <summary>
/// State of UI that lives OUTSIDE Unity (the React page around the WebGL canvas).
/// Lives in the ReusableCharacterController assembly so both the character scripts and the
/// game scripts (Assembly-CSharp) can read it. Set by CommunicationManager when React sends
/// "input.pointerOverUI".
/// </summary>
public static class ExternalUIState
{
    /// <summary>True while the mouse is over a React panel that sits on top of the canvas.
    /// Camera and click scripts ignore clicks/scroll/hover while this is true.</summary>
    public static bool PointerOverUI;
}
