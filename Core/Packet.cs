namespace LegendaryTools.Networking
{
    /// <summary>
    /// Clients send requests to the server and receive responses back. Forwarded calls arrive as-is.
    /// </summary>
    public enum Packet : byte
    {
        /// <summary>
        /// Empty packet. Can be used to keep the connection alive.
        /// </summary>
        Empty,

        /// <summary>
        /// This packet indicates that an error has occurred.
        /// string: Description of the error.
        /// </summary>
        Error,

        /// <summary>
        /// This packet indicates that the connection should be severed.
        /// </summary>
        Disconnect,

        //===================================================================================

        /// <summary>
        /// This should be the very first packet sent by the client.
        /// int32: Protocol version.
        /// </summary>
        RequestID,

        /// <summary>
        /// Always the first packet to arrive from the server.
        /// If the protocol version didn't match the client, a disconnect may follow.
        /// int32: Protocol ID.
        /// int32: Player ID (only if the protocol ID matched).
        /// int64: Server time in milliseconds (only if the protocol ID matched).
        /// </summary>
        ResponseID,
        
        KeepAlive,
        
        CommandMessage,
        RequestMessage,
        
        AddressableInstantiate,
        ResourcesInstantiate,
        Destroy,
        SyncVar,
        RPC,
    }
}