namespace RukayaRace.Flight
{
    /// <summary>
    /// Flight stance for a drafted Rukaya. Only two entries, and <see cref="Never"/> must stay 0 so
    /// older saves and freshly generated pawns default to "grounded".
    /// </summary>
    public enum RukayaFlightMode
    {
        /// <summary>Drafted pawns stay on the ground (default).</summary>
        Never = 0,

        /// <summary>Drafted pawns take off and keep flying.</summary>
        Always = 1
    }
}
